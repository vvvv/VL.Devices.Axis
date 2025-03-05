using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibVLCSharp;

// Workaround for https://code.videolan.org/videolan/LibVLCSharp/-/issues/650
class MyMediaPlayer : MediaPlayer
{
    public MyMediaPlayer(LibVLC libVLC) : base(libVLC)
    {
    }

    public MyMediaPlayer(LibVLC libvlc, Media media) : base(libvlc, media)
    {
    }

    protected override void Dispose(bool disposing)
    {
        // Backup current GC handle
        ref GCHandle _gcHandle = ref GetGCHandle(this);
        var handle = _gcHandle;

        // Set _gcHandle field to default so it doesn't get deleted before the native reference is released
        _gcHandle = default;

        // Release native reference (will invoke user callbacks like OutputCleanup)
        base.Dispose(disposing);

        // Delete the handle
        if (disposing && handle.IsAllocated)
        {
            handle.Free();
        }

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_gcHandle")]
        static extern ref GCHandle GetGCHandle(MediaPlayer instance);
    }
}
