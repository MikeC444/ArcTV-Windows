using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ArcTV.Core.Torrents;

/// <summary>What the local video server needs to know about a torrent that is still arriving: which bytes of the file are on disk, and a way to ask for the ones it needs next.</summary>
public interface IPieceSource
{
    /// <summary>True when every byte in [offset, offset + length) of the file has arrived.</summary>
    bool Have(long offset, long length);

    /// <summary>Asks for the pieces from this byte on first (the player started reading, or jumped, there).</summary>
    void Request(long offset);
}

/// <summary>A byte range asked for in an HTTP Range header.</summary>
public readonly record struct ByteRange(long Start, long End)
{
    /// <summary>
    /// Reads "bytes=0-99", "bytes=500-" (to the end) and "bytes=-200" (the last 200). Null when there is no header or it is not one range this file can answer (a player then gets the whole
    /// file); a range that starts past the end is reported as <paramref name="unsatisfiable"/>.
    /// </summary>
    public static ByteRange? Parse(string? header, long size, out bool unsatisfiable)
    {
        unsatisfiable = false;
        if (string.IsNullOrWhiteSpace(header) || size <= 0) return null;
        var text = header.Trim();
        if (!text.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = text[6..].Split(',')[0].Trim(); // the first range only: players ask for one
        var dash = spec.IndexOf('-');
        if (dash < 0) return null;
        var from = spec[..dash].Trim();
        var to = spec[(dash + 1)..].Trim();
        if (from.Length == 0)
        {
            if (!long.TryParse(to, out var last) || last <= 0) return null;
            return new ByteRange(Math.Max(0, size - last), size - 1);
        }

        if (!long.TryParse(from, out var start) || start < 0) return null;
        if (start >= size)
        {
            unsatisfiable = true;
            return null;
        }

        if (to.Length == 0) return new ByteRange(start, size - 1);
        if (!long.TryParse(to, out var end) || end < start) return null;
        return new ByteRange(start, Math.Min(end, size - 1));
    }
}

/// <summary>
/// A tiny web server on this PC only (127.0.0.1) that hands the player a torrent's video as its pieces arrive. The player asks for byte ranges (it seeks); each answer waits until the pieces for
/// that range are on disk, and asks the torrent engine to fetch them first. The same as the Mac app's, so the libtorrent engine gives the player what MonoTorrent's own stream does.
/// </summary>
public sealed class LocalVideoServer : IDisposable
{
    private const int Chunk = 256 * 1024;
    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(100);

    private readonly string _path;
    private readonly long _size;
    private readonly IPieceSource _pieces;
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public LocalVideoServer(string path, long size, IPieceSource pieces)
    {
        _path = path;
        _size = size;
        _pieces = pieces;
    }

    /// <summary>Starts listening and returns the address to give the player.</summary>
    public Uri Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptAsync);
        return new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/video{Path.GetExtension(_path)}");
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // already closed
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                _ = Task.Run(async () =>
                {
                    using (client) await ServeAsync(client).ConfigureAwait(false);
                });
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // stopped
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            var request = await ReadHeadAsync(stream).ConfigureAwait(false);
            if (request is null) return;
            var lines = request.Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 2) return;
            var method = first[0].ToUpperInvariant();
            string? rangeHeader = null;
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0 && line[..colon].Trim().Equals("range", StringComparison.OrdinalIgnoreCase)) rangeHeader = line[(colon + 1)..].Trim();
            }

            if (method is not ("GET" or "HEAD"))
            {
                await WriteTextAsync(stream, "HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                return;
            }

            var range = ByteRange.Parse(rangeHeader, _size, out var unsatisfiable);
            if (unsatisfiable)
            {
                await WriteTextAsync(stream, $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{_size}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n").ConfigureAwait(false);
                return;
            }

            var start = range?.Start ?? 0;
            var last = range?.End ?? _size - 1;
            var head = new StringBuilder(range is null ? "HTTP/1.1 200 OK\r\n" : "HTTP/1.1 206 Partial Content\r\n");
            head.Append($"Content-Type: {ContentType(_path)}\r\nAccept-Ranges: bytes\r\nContent-Length: {last - start + 1}\r\n");
            if (range is not null) head.Append($"Content-Range: bytes {start}-{last}/{_size}\r\n");
            head.Append("Connection: close\r\n\r\n");
            await WriteTextAsync(stream, head.ToString()).ConfigureAwait(false);
            if (method == "HEAD") return;

            await SendAsync(stream, start, last).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // the player closed the connection (it seeks, or it is done)
        }
    }

    private async Task SendAsync(NetworkStream stream, long start, long last)
    {
        _pieces.Request(start);
        FileStream? file = null;
        try
        {
            var position = start;
            var chunks = 0;
            var buffer = new byte[Chunk];
            while (position <= last && !_stop.IsCancellationRequested)
            {
                var length = (int)Math.Min(Chunk, last - position + 1);
                var waited = 0;
                while (!_pieces.Have(position, length))
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    await Task.Delay(PollEvery, _stop.Token).ConfigureAwait(false);
                    if (++waited % 20 == 0) _pieces.Request(position); // every couple of seconds: still waiting here, ask again
                }

                file ??= OpenWhenThere();
                if (file is null)
                {
                    await Task.Delay(200, _stop.Token).ConfigureAwait(false);
                    continue;
                }

                file.Seek(position, SeekOrigin.Begin);
                var read = await file.ReadAsync(buffer.AsMemory(0, length), _stop.Token).ConfigureAwait(false);
                if (read <= 0) return;
                await stream.WriteAsync(buffer.AsMemory(0, read), _stop.Token).ConfigureAwait(false);
                position += read;
                if (++chunks % 4 == 0) _pieces.Request(position); // keep the next pieces coming ahead of the player
            }
        }
        finally
        {
            file?.Dispose();
        }
    }

    /// <summary>The file, once the torrent engine has made it (it writes it while this reads, so sharing is wide open).</summary>
    private FileStream? OpenWhenThere()
    {
        try
        {
            return new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or IOException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadHeadAsync(NetworkStream stream)
    {
        var bytes = new List<byte>(512);
        var one = new byte[1024];
        while (bytes.Count < 65536)
        {
            var read = await stream.ReadAsync(one).ConfigureAwait(false);
            if (read <= 0) return null;
            bytes.AddRange(one.AsSpan(0, read).ToArray());
            var text = Encoding.ASCII.GetString(bytes.ToArray());
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end >= 0) return text[..end];
        }

        return null;
    }

    private static Task WriteTextAsync(NetworkStream stream, string text) => stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".mkv" => "video/x-matroska",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        _ => "application/octet-stream",
    };
}
