using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ArcTV.Core.Torrents;

/// <summary>How the torrent is doing (from libtorrent, through ctorrent).</summary>
public sealed record LibTorrentStatus(bool HasMetadata, int State, int Peers, int Seeds, double Progress, long DownloadRate, int FileCount, int Connections, int KnownPeers, int DhtNodes)
{
    /// <summary>0 checking the files on disk, 1 getting the file list, 2 downloading, 3 finished or seeding.</summary>
    public string StateName => State switch { 0 => "checking", 1 => "getting the file list", 2 => "downloading", _ => "finished" };
}

/// <summary>A file inside a torrent.</summary>
public sealed record LibTorrentFile(int Index, string Path, long Size);

/// <summary>The native calls (ctorrent.dll: a small C face on libtorrent, built from the repository's native folder).</summary>
internal static class NativeTorrent
{
    internal const string Library = "ctorrent";

    [StructLayout(LayoutKind.Sequential)]
    internal struct Status
    {
        public int HasMetadata;
        public int State;
        public int NumPeers;
        public int NumSeeds;
        public double Progress;
        public long DownloadRate;
        public int FileCount;
        public int NumConnections;
        public int ListPeers;
        public int DhtNodes;
    }

    [DllImport(Library, EntryPoint = "ct_create")]
    internal static extern IntPtr Create();

    [DllImport(Library, EntryPoint = "ct_destroy")]
    internal static extern void Destroy(IntPtr engine);

    [DllImport(Library, EntryPoint = "ct_start")]
    internal static extern int Start(IntPtr engine, [MarshalAs(UnmanagedType.LPUTF8Str)] string magnet, [MarshalAs(UnmanagedType.LPUTF8Str)] string savePath);

    [DllImport(Library, EntryPoint = "ct_stop")]
    internal static extern void Stop(IntPtr engine, int deleteFiles);

    [DllImport(Library, EntryPoint = "ct_get_status")]
    internal static extern void GetStatus(IntPtr engine, out Status status);

    [DllImport(Library, EntryPoint = "ct_file_info")]
    internal static extern int FileInfo(IntPtr engine, int index, byte[] name, int nameLength, out long size);

    [DllImport(Library, EntryPoint = "ct_file_path")]
    internal static extern int FilePath(IntPtr engine, int index, byte[] path, int pathLength);

    [DllImport(Library, EntryPoint = "ct_select_file")]
    internal static extern int SelectFile(IntPtr engine, int index);

    [DllImport(Library, EntryPoint = "ct_have_range")]
    internal static extern int HaveRange(IntPtr engine, int index, long offset, long length);

    [DllImport(Library, EntryPoint = "ct_stream_from")]
    internal static extern void StreamFrom(IntPtr engine, int index, long offset, int pieces);

    [DllImport(Library, EntryPoint = "ct_piece_length")]
    internal static extern int PieceLength(IntPtr engine);
}

/// <summary>
/// One libtorrent session holding one torrent at a time (the one being watched), the same shape as the Mac app's engine. Loading the native library can fail (the DLL is missing, or is for
/// another kind of PC): <see cref="Available"/> says so up front, and the app then keeps using MonoTorrent.
/// </summary>
public sealed class LibTorrentSession : IDisposable
{
    private static string? _folder;
    private static bool _resolverSet;
    private IntPtr _engine;

    /// <summary>The folder the native library is looked for in first (the app's own folder when unset), then the system's usual places.</summary>
    public static void UseFolder(string? folder)
    {
        _folder = folder;
        if (_resolverSet) return;
        _resolverSet = true;
        NativeLibrary.SetDllImportResolver(typeof(NativeTorrent).Assembly, Resolve);
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != NativeTorrent.Library) return IntPtr.Zero;
        var explicitPath = Environment.GetEnvironmentVariable("ARCTV_CTORRENT_PATH"); // for tests and odd setups: the full path of the library
        if (!string.IsNullOrEmpty(explicitPath) && NativeLibrary.TryLoad(explicitPath, out var chosen)) return chosen;
        var folder = _folder ?? AppContext.BaseDirectory;
        var file = OperatingSystem.IsWindows() ? "ctorrent.dll" : OperatingSystem.IsMacOS() ? "libctorrent.dylib" : "libctorrent.so";
        return NativeLibrary.TryLoad(Path.Combine(folder, file), out var handle) ? handle : IntPtr.Zero; // zero: the usual search then runs
    }

    /// <summary>True when the native library (and everything it needs) loads. <paramref name="why"/> says what failed when not.</summary>
    public static bool Available(out string? why)
    {
        try
        {
            UseFolder(_folder);
            using var session = new LibTorrentSession();
            why = null;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException)
        {
            why = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public LibTorrentSession()
    {
        UseFolder(_folder);
        _engine = NativeTorrent.Create();
        if (_engine == IntPtr.Zero) throw new InvalidOperationException("libtorrent could not start its session.");
    }

    private IntPtr Engine => _engine != IntPtr.Zero ? _engine : throw new ObjectDisposedException(nameof(LibTorrentSession));

    /// <summary>Replaces any torrent in the session with this one. Throws when the magnet link is not one.</summary>
    public void Start(string magnet, string saveFolder)
    {
        var code = NativeTorrent.Start(Engine, magnet, saveFolder);
        if (code != 0) throw new InvalidOperationException(code == -2 ? "That is not a magnet link libtorrent can read." : "libtorrent could not start the torrent.");
    }

    /// <summary>Stops the torrent; with <paramref name="deleteFiles"/> what it downloaded goes too.</summary>
    public void Stop(bool deleteFiles)
    {
        if (_engine != IntPtr.Zero) NativeTorrent.Stop(_engine, deleteFiles ? 1 : 0);
    }

    public LibTorrentStatus Status()
    {
        NativeTorrent.GetStatus(Engine, out var s);
        return new LibTorrentStatus(s.HasMetadata != 0, s.State, s.NumPeers, s.NumSeeds, s.Progress, s.DownloadRate, s.FileCount, s.NumConnections, s.ListPeers, s.DhtNodes);
    }

    public IReadOnlyList<LibTorrentFile> Files()
    {
        var count = Status().FileCount;
        var files = new List<LibTorrentFile>(count);
        var buffer = new byte[2048];
        for (var i = 0; i < count; i++)
        {
            if (NativeTorrent.FileInfo(Engine, i, buffer, buffer.Length, out var size) == 0) files.Add(new LibTorrentFile(i, Text(buffer), size));
        }

        return files;
    }

    /// <summary>Where the file is (or will be) on disk.</summary>
    public string FilePath(int index)
    {
        var buffer = new byte[4096];
        if (NativeTorrent.FilePath(Engine, index, buffer, buffer.Length) != 0) throw new InvalidOperationException("libtorrent does not know that file.");
        return Text(buffer);
    }

    /// <summary>Download only this file.</summary>
    public void SelectFile(int index)
    {
        if (NativeTorrent.SelectFile(Engine, index) != 0) throw new InvalidOperationException("libtorrent could not choose that file.");
    }

    public bool Have(int index, long offset, long length) => _engine != IntPtr.Zero && NativeTorrent.HaveRange(_engine, index, offset, length) == 1;

    public void StreamFrom(int index, long offset, int pieces)
    {
        if (_engine != IntPtr.Zero) NativeTorrent.StreamFrom(_engine, index, offset, pieces);
    }

    /// <summary>The size of a piece in bytes (0 until the file list is known).</summary>
    public int PieceLength() => _engine != IntPtr.Zero ? NativeTorrent.PieceLength(_engine) : 0;

    /// <summary>The video's side of the local server: which bytes have arrived, and asking for the ones needed next.</summary>
    public IPieceSource Source(int fileIndex, int piecesAhead) => new FileSource(this, fileIndex, piecesAhead);

    private static string Text(byte[] buffer)
    {
        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    public void Dispose()
    {
        var engine = Interlocked.Exchange(ref _engine, IntPtr.Zero);
        if (engine != IntPtr.Zero) NativeTorrent.Destroy(engine);
    }

    private sealed class FileSource(LibTorrentSession session, int index, int piecesAhead) : IPieceSource
    {
        public bool Have(long offset, long length) => session.Have(index, offset, length);

        public void Request(long offset) => session.StreamFrom(index, offset, piecesAhead);
    }
}
