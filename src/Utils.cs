namespace VL.Devices.Axis;

internal static class Utils
{
    private static bool initialized;

    public static void TryInitFromNuGetFolder(AppHost appHost)
    {
        if (initialized)
            return;

        initialized = true;

        var path = appHost.GetPackagePath("VideoLAN.LibVLC.Windows");
        if (path is null)
            return;

        var vlcPath = Path.Combine(path, "build", IntPtr.Size == 8 ? "x64" : "x86");
        LibVLCSharp.Core.Initialize(vlcPath);
    }
}
