using System.Runtime.Versioning;
using Windows.Win32.Graphics.Direct3D11;
using static Windows.Win32.Graphics.Direct3D11.D3D11_RTV_DIMENSION;

namespace VL.Devices.Axis;

[SupportedOSPlatform("windows7.0")]
internal unsafe class PooledTexture : IDisposable
{
    public required TexturePool Pool { get; init; }
    public required ID3D11Texture2D* Texture { get; init; }
    public required ID3D11RenderTargetView* RTV { get; init; }

    public D3D11_TEXTURE2D_DESC Description => Pool.Description;

    public void Recycle()
    {
        Pool.Return(this);
    }

    public void Dispose()
    {
        RTV->Release();
        Texture->Release();
    }
}

[SupportedOSPlatform("windows7.0")]
internal unsafe sealed class TexturePool : IDisposable
{
    private readonly Stack<PooledTexture> pool = new();
    private readonly ID3D11Device* _device;
    private readonly D3D11_TEXTURE2D_DESC _description;
    private bool isDisposed;

    public TexturePool(ID3D11Device* device, D3D11_TEXTURE2D_DESC description)
    {
        _device = device;
        _description = description;
        _device->AddRef();
    }

    public D3D11_TEXTURE2D_DESC Description => _description;

    public PooledTexture Rent()
    {
        lock (pool)
        {
            if (pool.Count > 0)
            {
                var surface = pool.Pop();
                return surface;
            }
            ID3D11Texture2D* texture2D;
            _device->CreateTexture2D(_description, null, &texture2D);

            ID3D11RenderTargetView* rtv;
            var renderTargetViewDesc = new D3D11_RENDER_TARGET_VIEW_DESC
            {
                Format = _description.Format,
                ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D
            };

            _device->CreateRenderTargetView((ID3D11Resource*)texture2D, &renderTargetViewDesc, &rtv);
            return new PooledTexture()
            { 
                Pool = this,
                Texture = texture2D,
                RTV = rtv
            };
        }
    }

    public void Return(PooledTexture surface)
    {
        lock (pool)
        {
            if (isDisposed || pool.Count > 16)
            {
                surface.Dispose();
            }
            else
            {
                pool.Push(surface);
            }
        }
    }
    private void Recycle()
    {
        lock (pool)
        {
            foreach (var s in pool)
                s.Dispose();

            pool.Clear();
        }
    }

    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            Recycle();
            _device->Release();
        }
    }
}