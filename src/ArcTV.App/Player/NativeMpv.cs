using System.Runtime.InteropServices;

namespace ArcTV.App.Player;

/// <summary>The few calls of libmpv (its C interface, in libmpv-2.dll beside the app) that the player uses.</summary>
internal static class NativeMpv
{
    private const string Library = "libmpv-2.dll";

    public const int FormatString = 1;
    public const int FormatFlag = 3;
    public const int FormatDouble = 5;

    public const int EventShutdown = 1;
    public const int EventEndFile = 7;
    public const int EventFileLoaded = 8;
    public const int EventPlaybackRestart = 21;
    public const int EventPropertyChange = 22;

    public const int EndFileEof = 0;
    public const int EndFileError = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Event
    {
        public int EventId;
        public int Error;
        public ulong ReplyUserdata;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventProperty
    {
        public IntPtr Name;
        public int Format;
        public IntPtr Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct EventEndFile
    {
        public int Reason;
        public int Error;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_create();

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_initialize(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_terminate_destroy(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_option_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_set_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_get_property_string(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    /// <summary><paramref name="arguments"/> points to an array of UTF-8 strings ending with a null pointer.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_command(IntPtr handle, IntPtr arguments);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern int mpv_observe_property(IntPtr handle, ulong replyUserdata, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_wait_event(IntPtr handle, double timeout);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_wakeup(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void mpv_free(IntPtr data);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr mpv_error_string(int error);
}
