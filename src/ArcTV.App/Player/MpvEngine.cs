using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ArcTV.App.Player;

/// <summary>A video engine: starts a video in a window and reports how it is getting on. The real one is mpv; the tests use a stand-in.</summary>
public interface IVideoEngine : ArcTV.Core.Player.IPlayerCommands
{
    event Action<double>? Position;

    event Action<double>? Duration;

    event Action<bool>? Paused;

    event Action<bool>? Buffering;

    event Action<bool>? Seeking;

    /// <summary>The first picture is up.</summary>
    event Action? Started;

    event Action? Ended;

    event Action<string>? Failed;

    /// <summary>Starts <paramref name="url"/> in the window <paramref name="window"/> (a native handle), from <paramref name="startAt"/> seconds in. Reports a problem through <see cref="Failed"/>.</summary>
    void Start(IntPtr window, Uri url, double startAt);
}

/// <summary>
/// The player's engine: mpv (libmpv-2.dll), drawing into a window of ours. Its events are read on a thread of its own and handed over to the thread that made it (the UI thread),
/// so everything it raises is safe to show.
/// </summary>
public sealed class MpvEngine : IVideoEngine, IDisposable
{
    public const string MissingMessage = "The video engine (libmpv) was not found next to the app. Run scripts\\get-libmpv.ps1 (it downloads it), then start Arc TV again.";

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private IntPtr _handle;
    private Thread? _events;
    private volatile bool _stop;
    private bool _disposed;

    public event Action<double>? Position;

    public event Action<double>? Duration;

    public event Action<bool>? Paused;

    public event Action<bool>? Buffering;

    public event Action<bool>? Seeking;

    public event Action? Started;

    public event Action? Ended;

    public event Action<string>? Failed;

    /// <summary>Whether libmpv-2.dll can be loaded here.</summary>
    public static bool IsAvailable()
    {
        try
        {
            if (!NativeLibrary.TryLoad("libmpv-2.dll", typeof(MpvEngine).Assembly, DllImportSearchPath.AssemblyDirectory, out var library)) return false;
            NativeLibrary.Free(library);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Start(IntPtr window, Uri url, double startAt)
    {
        try
        {
            _handle = NativeMpv.mpv_create();
            if (_handle == IntPtr.Zero)
            {
                Failed?.Invoke("The player could not be started.");
                return;
            }

            Option("wid", ((long)window).ToString(CultureInfo.InvariantCulture));
            // our own controls, our own keys: the engine only draws the picture
            Option("osc", "no");
            Option("input-default-bindings", "no");
            Option("input-vo-keyboard", "no");
            Option("input-cursor", "no");
            Option("cursor-autohide", "no");
            Option("ytdl", "no");
            Option("keep-open", "no");
            Option("idle", "no");
            // speed first: the hardware decoder, cheap scaling and no extras that need heavy work per frame (options this mpv doesn't know are ignored)
            Option("hwdec", "auto-safe");
            Option("vo", "gpu");
            foreach (var (name, value) in new[]
            {
                ("scale", "bilinear"), ("dscale", "bilinear"), ("cscale", "bilinear"), ("correct-downscaling", "no"), ("linear-downscaling", "no"),
                ("sigmoid-upscaling", "no"), ("deband", "no"), ("dither-depth", "no"), ("interpolation", "no"), ("hdr-compute-peak", "no"),
                ("video-sync", "audio"), ("framedrop", "vo"),
                // a bigger read-ahead so a slow patch on the network is ridden out instead of stuttering
                ("cache", "yes"), ("demuxer-max-bytes", "300MiB"), ("demuxer-max-back-bytes", "100MiB"), ("demuxer-readahead-secs", "60"),
                ("cache-pause-initial", "no"), ("network-timeout", "30"),
                ("subs-match-os-language", "yes"), ("subs-fallback", "yes"), ("sid", "no"),
            }) Option(name, value);
            if (startAt > 1) Option("start", ((int)startAt).ToString(CultureInfo.InvariantCulture));

            if (NativeMpv.mpv_initialize(_handle) < 0)
            {
                Failed?.Invoke("The player could not be started.");
                return;
            }

            NativeMpv.mpv_observe_property(_handle, 0, "time-pos", NativeMpv.FormatDouble);
            NativeMpv.mpv_observe_property(_handle, 0, "duration", NativeMpv.FormatDouble);
            NativeMpv.mpv_observe_property(_handle, 0, "pause", NativeMpv.FormatFlag);
            NativeMpv.mpv_observe_property(_handle, 0, "paused-for-cache", NativeMpv.FormatFlag);
            NativeMpv.mpv_observe_property(_handle, 0, "seeking", NativeMpv.FormatFlag);

            _events = new Thread(ReadEvents) { IsBackground = true, Name = "mpv events" };
            _events.Start();
            Command("loadfile", url.AbsoluteUri, "replace");
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            _handle = IntPtr.Zero;
            Failed?.Invoke(MissingMessage);
        }
    }

    private void Option(string name, string value) => NativeMpv.mpv_set_option_string(_handle, name, value); // an option this build does not know is ignored

    // ---- what is reported ----

    private void Report(Action action)
    {
        if (_stop) return;
        if (_ui is null) action();
        else _ui.Post(_ => { if (!_stop) action(); }, null);
    }

    private void ReadEvents()
    {
        while (!_stop)
        {
            var pointer = NativeMpv.mpv_wait_event(_handle, 0.25);
            if (pointer == IntPtr.Zero) continue;
            var ev = Marshal.PtrToStructure<NativeMpv.Event>(pointer);
            switch (ev.EventId)
            {
                case NativeMpv.IdShutdown:
                    return;
                case NativeMpv.IdPropertyChange when ev.Data != IntPtr.Zero:
                    ReadProperty(Marshal.PtrToStructure<NativeMpv.EventProperty>(ev.Data));
                    break;
                case NativeMpv.IdFileLoaded or NativeMpv.IdPlaybackRestart:
                    Report(() => Started?.Invoke());
                    break;
                case NativeMpv.IdEndFile when ev.Data != IntPtr.Zero:
                    var end = Marshal.PtrToStructure<NativeMpv.EventEndFile>(ev.Data);
                    if (end.Reason == NativeMpv.EndFileEof) Report(() => Ended?.Invoke());
                    else if (end.Reason == NativeMpv.EndFileError) Report(() => Failed?.Invoke("This source could not be played. Try another source."));
                    break;
            }
        }
    }

    private void ReadProperty(NativeMpv.EventProperty property)
    {
        if (property.Data == IntPtr.Zero) return;
        var name = Marshal.PtrToStringUTF8(property.Name);
        switch (name, property.Format)
        {
            case ("time-pos", NativeMpv.FormatDouble):
                var position = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(property.Data));
                Report(() => Position?.Invoke(position));
                break;
            case ("duration", NativeMpv.FormatDouble):
                var duration = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(property.Data));
                Report(() => Duration?.Invoke(duration));
                break;
            case ("pause", NativeMpv.FormatFlag):
                var paused = Marshal.ReadInt32(property.Data) != 0;
                Report(() => Paused?.Invoke(paused));
                break;
            case ("paused-for-cache", NativeMpv.FormatFlag):
                var buffering = Marshal.ReadInt32(property.Data) != 0;
                Report(() => Buffering?.Invoke(buffering));
                break;
            case ("seeking", NativeMpv.FormatFlag):
                var seeking = Marshal.ReadInt32(property.Data) != 0;
                Report(() => Seeking?.Invoke(seeking));
                break;
        }
    }

    // ---- what it is told ----

    private void Command(params string[] arguments)
    {
        if (_handle == IntPtr.Zero) return;
        var strings = new IntPtr[arguments.Length + 1];
        var list = IntPtr.Zero;
        try
        {
            for (var i = 0; i < arguments.Length; i++) strings[i] = Marshal.StringToCoTaskMemUTF8(arguments[i]);
            list = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
            Marshal.Copy(strings, 0, list, strings.Length);
            NativeMpv.mpv_command(_handle, list);
        }
        finally
        {
            foreach (var pointer in strings) if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
        }
    }

    private void SetString(string name, string value)
    {
        if (_handle != IntPtr.Zero) NativeMpv.mpv_set_property_string(_handle, name, value);
    }

    public string? GetString(string property)
    {
        if (_handle == IntPtr.Zero) return null;
        var raw = NativeMpv.mpv_get_property_string(_handle, property);
        if (raw == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(raw);
        }
        finally
        {
            NativeMpv.mpv_free(raw);
        }
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public void SetPause(bool paused) => SetString("pause", paused ? "yes" : "no");

    public void SeekTo(double seconds) => Command("seek", Number(seconds), "absolute");

    public void SeekBy(double seconds) => Command("seek", Number(seconds), "relative");

    public void SetVolume(double volume) => SetString("volume", ((int)volume).ToString(CultureInfo.InvariantCulture));

    public void SetSpeed(double speed) => SetString("speed", Number(speed));

    public void SetAudioTrack(int? id) => SetString("aid", id?.ToString(CultureInfo.InvariantCulture) ?? "no");

    public void SetSubtitleTrack(int? id) => SetString("sid", id?.ToString(CultureInfo.InvariantCulture) ?? "no");

    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop = true;
        var handle = _handle;
        _handle = IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        try
        {
            NativeMpv.mpv_wakeup(handle);
            _events?.Join(1500);
            NativeMpv.mpv_terminate_destroy(handle);
        }
        catch (Exception)
        {
            // closing a player that is already gone is not worth reporting
        }
    }
}
