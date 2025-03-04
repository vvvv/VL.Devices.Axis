using LibVLCSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using static Windows.Win32.Graphics.Direct3D11.D3D11_USAGE;
using static Windows.Win32.Graphics.Direct3D11.D3D11_BIND_FLAG;
using static Windows.Win32.Graphics.Direct3D11.D3D11_RESOURCE_MISC_FLAG;
using static Windows.Win32.Graphics.Dxgi.Common.DXGI_FORMAT;
using static Windows.Win32.PInvoke;
using System.Runtime.Versioning;
using static LibVLCSharp.MediaPlayer;
using System.Collections.Concurrent;
using VL.Lib.Basics.Imaging;

namespace VL.Devices.Axis;

[ProcessNode]
[SupportedOSPlatform("windows7.0")]
public unsafe sealed class VideoIn : IVideoSource2, IDisposable
{
    private readonly object syncObject = new object();
    private readonly ILogger logger;
    private readonly ID3D11Device* device;
    private readonly ID3D11DeviceContext* deviceContext;
    private string? url;
    private int changeTicket;
    private bool enabled;
    private bool isDisposed;
    private Acquisition? acquisition;

    public VideoIn([Pin(Visibility = PinVisibility.Optional)] NodeContext nodeContext)
    {
        Utils.TryInitFromNuGetFolder(nodeContext.AppHost);

        logger = nodeContext.GetLogger();

        ID3D11Device* device;
        ID3D11DeviceContext* deviceContext;

        D3D11CreateDevice(
            null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default, D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT, default, 0, 7,
            &device, null, &deviceContext).ThrowOnFailure();

        this.device = device;
        this.deviceContext = deviceContext;
    }

    public IVideoSource2? Update(string url, bool enabled = true)
    {
        if (isDisposed)
            return null;

        if (url != this.url || enabled != this.enabled)
        {
            this.url = url;
            this.enabled = enabled;
            this.acquisition = null;
            changeTicket++;
        }

        lock (syncObject)
        {
            var player = acquisition?.Player;
            if (player != null)
            {
                IsPlaying = player.IsPlaying;
                State = player.State;
                Time = (float)TimeSpan.FromMilliseconds(player.Time).TotalSeconds;
                Position = (float)player.Position;
                Length = (float)TimeSpan.FromMilliseconds(player.Length).TotalSeconds;
            }
            else
            {
                IsPlaying = false;
                State = VLCState.Stopped;
                Time = default;
                Position = default;
                Length = default;
            }
        }

        return this;
    }

    public bool IsPlaying { get; private set; }

    /// <summary>
    /// <inheritdoc cref="MediaPlayer.State"/>
    /// </summary>
    public VLCState State { get; private set; }

    /// <summary>
    /// The stream time in seconds.
    /// </summary>
    public float Time { get; private set; }

    /// <summary>
    /// <inheritdoc cref="MediaPlayer.Position"/>
    /// </summary>
    public float Position { get; private set; }

    /// <summary>
    /// The stream length in seconds.
    /// </summary>
    public float Length { get; private set; }

    int IVideoSource2.ChangedTicket => changeTicket;

    IVideoPlayer? IVideoSource2.Start(VideoPlaybackContext ctx)
    {
        if (url is null || isDisposed || !enabled)
            return null;

        return acquisition = new Acquisition(this, ctx, url, logger, device, deviceContext);
    }

    void IDisposable.Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        changeTicket++; // Enforce the sink to re-subscribe
        deviceContext->Release();
        device->Release();
    }

    private class Acquisition : IVideoPlayer
    {
        private readonly VideoIn videoPlayer;
        private readonly VideoPlaybackContext ctx;
        private readonly string url;
        private readonly LibVLC libVLC;
        private readonly MediaPlayer mediaPlayer;
        private readonly ID3D11Device* vvvvDevice;
        private readonly ID3D11Device* device;
        private readonly ID3D11DeviceContext* deviceContext;
        private readonly BlockingCollection<PooledTexture> frames = new(boundedCapacity: 2);

        private TexturePool? texturePool;

        public Acquisition(VideoIn videoPlayer, VideoPlaybackContext ctx, string url, ILogger logger, ID3D11Device* device, ID3D11DeviceContext* deviceContext)
        {
            this.videoPlayer = videoPlayer;
            this.ctx = ctx;
            this.url = url;
            this.device = device;
            this.deviceContext = deviceContext;

            if (ctx.GraphicsDeviceType == GraphicsDeviceType.Direct3D11)
                this.vvvvDevice = (ID3D11Device*)ctx.GraphicsDevice;

            libVLC = new LibVLC(enableDebugLogs: true);
            libVLC.Log += (sender, e) =>
            {
                logger.Log(e.Level switch
                {
                    LibVLCSharp.LogLevel.Debug => LogLevel.Debug,
                    LibVLCSharp.LogLevel.Error => LogLevel.Error,
                    LibVLCSharp.LogLevel.Warning => LogLevel.Warning,
                    _ => LogLevel.Information
                }, e.Message);
            };


            using var media = CreateMedia(url);
            mediaPlayer = new MediaPlayer(libVLC, media);
            mediaPlayer.SetOutputCallbacks(VideoEngine.D3D11, OutputSetup, OutputCleanup, OutputSetResize, UpdateOuput, Swap, StartRendering, null, null, SelectPlane);
            mediaPlayer.Play();

            static Media CreateMedia(string url)
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return new Media(uri);
                return new Media(url, type: FromType.FromPath);
            }
        }

        public MediaPlayer Player => mediaPlayer;

        void IDisposable.Dispose()
        {
            lock (videoPlayer.syncObject)
            {
                videoPlayer.acquisition = null;
                frames.CompleteAdding();
                mediaPlayer.StopAsync().Wait();
                texturePool?.Dispose();
                mediaPlayer.Dispose();
                libVLC.Dispose();
            }
        }

        IResourceProvider<VideoFrame>? IVideoPlayer.GrabVideoFrame()
        {
            if (!frames.TryTake(out var pooledTexture, millisecondsTimeout: 1000))
                return null;

            if (pooledTexture.AssociatedVideoTexture is null)
            {
                // Retrieve the shared handle
                HANDLE sharedHandle;
                {
                    pooledTexture.Texture->QueryInterface<IDXGIResource>(out var sharedResource);
                    sharedResource->GetSharedHandle(&sharedHandle);
                    sharedResource->Release();
                }

                // Open on D3D11 device of vvvv
                ID3D11Texture2D* texture;
                {
                    ID3D11Resource* sharedResource;
                    Guid iid = ID3D11Resource.IID_Guid;
                    vvvvDevice->OpenSharedResource(sharedHandle, &iid, (void**)&sharedResource);
                    sharedResource->QueryInterface(out texture);
                    sharedResource->Release();
                }

                var desc = pooledTexture.Description;
                pooledTexture.TextureOnMainDevice = texture;
                pooledTexture.AssociatedVideoTexture = new VideoTexture((nint)texture, (int)desc.Width, (int)desc.Height, ToPixelFormat(desc.Format));
            }

            return ResourceProvider.Return(new GpuVideoFrame<BgraPixel>(pooledTexture.AssociatedVideoTexture), pooledTexture, t => t.Recycle());

            static PixelFormat ToPixelFormat(DXGI_FORMAT format)
            {
                switch (format)
                {
                    case DXGI_FORMAT_B8G8R8A8_UNORM:
                    case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
                        return PixelFormat.B8G8R8A8;
                    case DXGI_FORMAT_R8G8B8A8_UNORM:
                    case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
                        return PixelFormat.R8G8B8A8;
                    default:
                        throw new NotImplementedException();
                }
            }
        }

        bool OutputSetup(ref IntPtr opaque, SetupDeviceConfig* config, ref SetupDeviceInfo setup)
        {
            setup.D3D11.DeviceContext = deviceContext;
            //deviceContext->AddRef();
            return true;
        }

        // TODO: Figure out why this callback is not always called
        void OutputCleanup(IntPtr opaque)
        {
            // here we can release all things Direct3D11 for good (if playing only one file)
            texturePool?.Dispose();
            texturePool = null;
            //deviceContext->Release();
        }

        void OutputSetResize(IntPtr opaque, ReportSizeChange report_size_change, IntPtr report_opaque)
        {
            //fixed (RTL_CRITICAL_SECTION* sl = &sizeLock)
            //{
            //    EnterCriticalSection(sl);

            //    if (report_size_change != null && report_opaque != IntPtr.Zero)
            //    {
            //        reportSize = report_size_change;
            //        reportOpaque = report_opaque;
            //        reportSize?.Invoke(reportOpaque, width, height);
            //    }

            //    LeaveCriticalSection(sl);
            //}
        }

        bool UpdateOuput(IntPtr opaque, RenderConfig* config, ref OutputConfig output)
        {
            texturePool?.Dispose();

            var renderFormat = DXGI_FORMAT_B8G8R8A8_UNORM;

            var texDesc = new D3D11_TEXTURE2D_DESC
            {
                MipLevels = 1,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE,
                Usage = D3D11_USAGE_DEFAULT,
                CPUAccessFlags = 0,
                ArraySize = 1,
                Format = ctx.UsesLinearColorspace ? DXGI_FORMAT_B8G8R8A8_UNORM_SRGB : renderFormat,
                Height = config->Height,
                Width = config->Width,
                MiscFlags = D3D11_RESOURCE_MISC_SHARED
            };
            texturePool = new TexturePool(device, texDesc);

            output.Union.DxgiFormat = (int)renderFormat;
            output.FullRange = true;
            output.ColorSpace = ColorSpace.BT709;
            output.ColorPrimaries = ColorPrimaries.BT709;
            output.TransferFunction = ctx.UsesLinearColorspace ? TransferFunction.LINEAR : TransferFunction.SRGB;
            output.Orientation = VideoOrientation.TopLeft;

            return true;
        }

        void Swap(IntPtr opaque)
        {
            var texture = Interlocked.Exchange(ref currentRenderTarget, null);
            if (texture is null)
                return;

            deviceContext->Flush();

            try
            {
                if (!frames.TryAdd(texture))
                    texture.Recycle();
            }
            catch (InvalidOperationException)
            {
                texture.Dispose();
            }
        }

        bool StartRendering(IntPtr opaque, bool enter)
        {
            if (enter)
            {
                currentRenderTarget = texturePool?.Rent();
                if (currentRenderTarget is null)
                    return false;

                var rtv = currentRenderTarget.RTV;
                deviceContext->OMSetRenderTargets(1, &rtv, null);
            }

            return true;
        }
        PooledTexture? currentRenderTarget;

        unsafe bool SelectPlane(IntPtr opaque, UIntPtr plane, void* output)
        {
            if (plane != default)
                return false;

            return true;
        }
    }
}
