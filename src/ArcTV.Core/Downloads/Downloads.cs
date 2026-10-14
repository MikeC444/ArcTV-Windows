using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ArcTV.Core.Details;
using ArcTV.Core.Mvvm;
using ArcTV.Core.Sources;

namespace ArcTV.Core.Downloads;

/// <summary>Choosing and naming what to save from a title's page (an Arc TV Plus feature), as on the Mac app.</summary>
/// <summary>A debrid service's short code as the addon writes it in the stream name ("RD"), and whether it already has the file stored.</summary>
public sealed record DebridTag(string Service, bool Cached)
{
    private static readonly Dictionary<string, string> Names = new() { ["RD"] = "Real-Debrid", ["AD"] = "AllDebrid", ["PM"] = "Premiumize", ["DL"] = "Debrid-Link", ["TB"] = "TorBox" };

    public string ServiceName => Names.GetValueOrDefault(Service.ToUpperInvariant(), Service.ToUpperInvariant());

    /// <summary>"Cached on Real-Debrid", or "Not cached: may take a while to start" for a file the service still has to fetch.</summary>
    public string Label => Cached ? $"Cached on {ServiceName}" : "Not cached: may take a while to start";
}

public static partial class DownloadChoices
{
    private static readonly HashSet<string> Extensions = ["mkv", "mp4", "m4v", "mov", "avi", "webm", "ts", "wmv"];
    private static readonly char[] NotInFileNames = ['/', '\\', ':', '?', '*', '"', '<', '>', '|', '\n', '\r'];

    /// <summary>The sources that are a plain web file (what a debrid service gives), best quality first and, within a quality, the smallest file first.</summary>
    public static IReadOnlyList<SourceItem> Sorted(IEnumerable<SourceItem> items) =>
        items.Where(item => item.IsPlayable).Select((item, index) => (item, index))
            .OrderByDescending(pair => pair.item.Quality).ThenBy(pair => pair.item.SizeBytes ?? long.MaxValue).ThenBy(pair => pair.index)
            .Select(pair => pair.item).ToList();

    /// <summary>A file name that is safe on disk: the release name without characters a file name can't hold, with the link's own extension (mkv if none).</summary>
    public static string FileName(string release, Uri url)
    {
        var words = release.Split(NotInFileNames).SelectMany(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var name = string.Join(' ', words).Trim('.', ' ');
        if (name.Length == 0) name = "Arc TV download";
        if (name.Length > 150) name = name[..150].Trim();
        var dotted = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        if (Extensions.Contains(dotted)) return name;
        var fromLink = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
        return name + "." + (Extensions.Contains(fromLink) ? fromLink : "mkv");
    }

    /// <summary>A Season choice in the Download panel: "Season 2 · 8 episodes".</summary>
    public static string SeasonLabel(int season, int episodes) => $"Season {season} · {(episodes == 1 ? "1 episode" : $"{episodes} episodes")}";

    /// <summary>What the panel calls an episode once it is picked (the same shape the title page uses): "S2 E3 The Title".</summary>
    public static string EpisodeHeading(EpisodeInfo episode) => $"S{episode.Season ?? 0} E{episode.Episode ?? 0} {episode.DisplayTitle}";

    /// <summary>What the panel says about a source, read from its release name: "UHD Blu-ray REMUX", "4K WEB-DL", "HDTV"... (the quality label when the name says nothing more).</summary>
    public static string Kind(SourceItem item)
    {
        var name = string.Join(' ', new[] { item.Headline, item.Stream.Name, item.Stream.BehaviorHints?.Filename }.Where(text => text is not null));
        var uhd = item.Quality == Quality.P2160;
        if (RemuxWord().IsMatch(name)) return (uhd ? "UHD " : "") + "Blu-ray REMUX";
        if (BluRayWord().IsMatch(name)) return (uhd ? "UHD " : "") + "Blu-ray";
        if (WebWord().IsMatch(name)) return (uhd ? "4K " : "") + "WEB-DL";
        if (HdtvWord().IsMatch(name)) return "HDTV";
        if (DvdWord().IsMatch(name)) return "DVD";
        if (CameraWord().IsMatch(name)) return "Camera recording";
        return item.Quality.Label();
    }

    private static string NameText(SourceItem item) =>
        string.Join(' ', new[] { item.Headline, item.Stream.Name, item.Stream.BehaviorHints?.Filename }.Where(text => text is not null));

    /// <summary>"Dolby Vision" or "HDR" when the release's name says so.</summary>
    public static string? Hdr(SourceItem item)
    {
        var name = NameText(item);
        if (DolbyVisionWord().IsMatch(name)) return "Dolby Vision";
        return HdrWord().IsMatch(name) ? "HDR" : null;
    }

    /// <summary>Read from the tag addons put at the start of a stream's name: "[RD+] ..." (the service has the file stored) or "[RD download] ..." (it has to fetch it first).</summary>
    public static DebridTag? Debrid(SourceItem item)
    {
        if (item.Stream.Name is not { } name || DebridTagWord().Match(name) is not { Success: true } match) return null;
        return new DebridTag(match.Groups[1].Value.ToUpperInvariant(), match.Groups[2].Value == "+");
    }

    /// <summary>A 1080p or 4K file under this size is almost never a real copy (a stub, a sample or a mislabelled file).</summary>
    public const long UnusuallySmallBytes = 150_000_000;

    public static bool IsUnusuallySmall(SourceItem item) =>
        item.Stream.BehaviorHints?.VideoSize is > 0 and var bytes && item.Quality is Quality.P2160 or Quality.P1080 && bytes < UnusuallySmallBytes;

    [GeneratedRegex(@"dolby[ ._-]?vision|\bdovi\b|\bDV\b", RegexOptions.IgnoreCase)]
    private static partial Regex DolbyVisionWord();

    [GeneratedRegex(@"\bhdr(10\+?)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex HdrWord();

    [GeneratedRegex(@"^\s*\[\s*([A-Za-z]{2,3})\s*(\+|download)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex DebridTagWord();

    [GeneratedRegex("remux", RegexOptions.IgnoreCase)]
    private static partial Regex RemuxWord();

    [GeneratedRegex(@"blu-?ray|bdrip|brrip|\bbd(25|50)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex BluRayWord();

    [GeneratedRegex(@"web-?dl|web-?rip|\bweb\b", RegexOptions.IgnoreCase)]
    private static partial Regex WebWord();

    [GeneratedRegex("hdtv", RegexOptions.IgnoreCase)]
    private static partial Regex HdtvWord();

    [GeneratedRegex("dvd", RegexOptions.IgnoreCase)]
    private static partial Regex DvdWord();

    [GeneratedRegex(@"\b(cam|hdcam|ts|hdts|telesync)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CameraWord();
}

public enum DownloadState
{
    Running,

    /// <summary>Stopped part way (Arc TV was closed): the half file is kept and Resume carries on from it.</summary>
    Paused,
    Done,
    Failed,
}

/// <summary>One file being saved: what it is called, how far it has got, and how it ended.</summary>
public sealed class DownloadJob : ObservableObject
{
    private double? _fraction;
    private DownloadState _state;
    private string? _message;
    private string? _filePath;

    internal DownloadJob(int id, string name, Uri url)
    {
        Id = id;
        Name = name;
        Url = url;
    }

    public int Id { get; }

    public string Name { get; }

    /// <summary>The link the file is fetched from (kept so a stopped download can carry on).</summary>
    public Uri Url { get; }

    /// <summary>0 to 1, or null while the size is not known.</summary>
    public double? Fraction { get => _fraction; internal set => Set(ref _fraction, value); }

    public DownloadState State { get => _state; internal set => Set(ref _state, value); }

    /// <summary>Why it failed or stopped, in words.</summary>
    public string? Message { get => _message; internal set => Set(ref _message, value); }

    /// <summary>Where the finished file is.</summary>
    public string? FilePath { get => _filePath; internal set => Set(ref _filePath, value); }

    /// <summary>Where the file will be (or is): chosen when the download starts, so carrying on writes the same file.</summary>
    internal string? Destination { get; set; }

    /// <summary>The whole file's size in bytes, once the server has said.</summary>
    internal long? Total { get; set; }

    internal CancellationTokenSource Stop { get; set; } = new();

    /// <summary>A stopped or broken download that still has its half file (or can start over) can be resumed.</summary>
    public bool CanResume => State is DownloadState.Paused || State == DownloadState.Failed && Destination is not null && Resumable;

    internal bool Resumable { get; set; }
}

/// <summary>What is kept about a download between runs of the app.</summary>
internal sealed record DownloadRecord(int Id, string Name, string Url, string? Destination, string State, string? Message, long? Total);

/// <summary>
/// Saves files from a title's page into the PC's Downloads folder and keeps what each one is doing. A file is written as "name.part" and renamed when it is whole. With a state file the list
/// is kept between runs: a download that was running when the app closed comes back as Paused and Resume carries on from the half file (the link is asked for the rest of the bytes).
/// </summary>
public sealed class DownloadManager
{
    private readonly HttpClient _http;
    private readonly string? _stateFile;
    private readonly object _saving = new();
    private int _next;

    public DownloadManager(HttpClient http, string folder, string? stateFile = null)
    {
        _http = http;
        Folder = folder;
        _stateFile = stateFile;
        Load();
    }

    /// <summary>Where files are saved (the person can change it; a file already being saved keeps the folder it started in).</summary>
    public string Folder { get; set; }

    public ObservableCollection<DownloadJob> Jobs { get; } = [];

    /// <summary>The Downloads folder of the signed-in Windows user.</summary>
    public static string DefaultFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Starts saving <paramref name="url"/> as <paramref name="fileName"/> (a number is added when that name is taken). Call it on the screen's thread: the job's changes are reported there.</summary>
    public DownloadJob Start(Uri url, string fileName)
    {
        var context = SynchronizationContext.Current;
        var job = new DownloadJob(Interlocked.Increment(ref _next), fileName, url);
        Jobs.Insert(0, job);
        _ = RunAsync(job, context, false);
        return job;
    }

    /// <summary>Carries a stopped or broken download on from its half file.</summary>
    public void Resume(DownloadJob job)
    {
        if (!job.CanResume) return;
        var context = SynchronizationContext.Current;
        job.Stop = new CancellationTokenSource();
        job.Message = null;
        job.State = DownloadState.Running;
        _ = RunAsync(job, context, true);
    }

    public void Cancel(DownloadJob job)
    {
        job.Stop.Cancel();
        Jobs.Remove(job);
        DeleteHalfFile(job);
        Save();
    }

    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(job => job.State is DownloadState.Done or DownloadState.Failed && !job.CanResume).ToList()) Jobs.Remove(job);
        Save();
    }

    /// <summary>Takes one finished or failed entry off the list (the file itself stays on disk).</summary>
    public void Remove(DownloadJob job)
    {
        if (job.State == DownloadState.Running) return;
        Jobs.Remove(job);
        if (job.State != DownloadState.Done) DeleteHalfFile(job);
        Save();
    }

    /// <summary>"Name.mkv", else "Name 2.mkv", "Name 3.mkv"...</summary>
    public static string UniquePath(string folder, string name)
    {
        var candidate = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var number = 2; File.Exists(candidate) || File.Exists(candidate + ".part"); number++) candidate = Path.Combine(folder, $"{stem} {number}{extension}");
        return candidate;
    }

    private static void DeleteHalfFile(DownloadJob job)
    {
        try
        {
            if (job.Destination is { } destination && File.Exists(destination + ".part")) File.Delete(destination + ".part");
        }
        catch (IOException)
        {
            // left for the next clean-up
        }
    }

    // ---- kept between runs ----

    private void Load()
    {
        if (_stateFile is null || !File.Exists(_stateFile)) return;
        List<DownloadRecord>? records;
        try
        {
            records = System.Text.Json.JsonSerializer.Deserialize<List<DownloadRecord>>(File.ReadAllText(_stateFile));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var record in records ?? [])
        {
            if (!Uri.TryCreate(record.Url, UriKind.Absolute, out var url)) continue;
            _next = Math.Max(_next, record.Id);
            var part = record.Destination is null ? null : record.Destination + ".part";
            var state = Enum.TryParse<DownloadState>(record.State, out var parsed) ? parsed : DownloadState.Failed;
            // a download that was running when the app closed was stopped by that
            if (state == DownloadState.Running) state = DownloadState.Paused;
            if (state == DownloadState.Done && (record.Destination is null || !File.Exists(record.Destination))) continue; // the file was moved or deleted
            if (state == DownloadState.Failed && (part is null || !File.Exists(part))) continue; // a failure with nothing to carry on from is not worth keeping
            var job = new DownloadJob(record.Id, record.Name, url)
            {
                Destination = record.Destination, Total = record.Total, State = state, Resumable = state == DownloadState.Paused || part is not null && File.Exists(part),
                Message = state == DownloadState.Paused ? "Stopped when Arc TV closed. Resume to carry on." : record.Message,
            };
            if (state == DownloadState.Done) (job.FilePath, job.Fraction) = (record.Destination, 1);
            else if (part is not null && File.Exists(part) && record.Total is > 0) job.Fraction = Math.Min(1.0, (double)new FileInfo(part).Length / record.Total.Value);
            Jobs.Add(job);
        }
    }

    private void Save()
    {
        if (_stateFile is null) return;
        var records = Jobs.Select(job => new DownloadRecord(job.Id, job.Name, job.Url.AbsoluteUri, job.Destination, job.State.ToString(), job.Message, job.Total)).ToList();
        lock (_saving)
        {
            try
            {
                var temporary = _stateFile + ".tmp";
                File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(records));
                File.Move(temporary, _stateFile, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the list is only a convenience: not keeping it is not worth stopping a download for
            }
        }
    }

    // ---- fetching ----

    private async Task RunAsync(DownloadJob job, SynchronizationContext? context, bool resume)
    {
        void Post(Action change)
        {
            if (context is null) change();
            else context.Post(_ => change(), null);
        }

        string? part = null;
        var keepHalf = resume && job.Destination is { } earlier && File.Exists(earlier + ".part"); // a resumed download's half file survives a failed first attempt (no connection, say)
        try
        {
            if (job.Destination is null)
            {
                var where = Folder;
                Directory.CreateDirectory(where);
                job.Destination = UniquePath(where, job.Name);
            }

            var destination = job.Destination;
            part = destination + ".part";
            Post(Save);

            var from = resume && File.Exists(part) ? new FileInfo(part).Length : 0;
            HttpResponseMessage response;
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
                if (from > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, null);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, job.Stop.Token).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && from > 0)
                {
                    // the half file is not what the server expects (or is already whole): start again from the beginning
                    response.Dispose();
                    from = 0;
                    continue;
                }

                break;
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var code = (int)response.StatusCode;
                    var message = code is 401 or 403 or 404 or 410
                        ? "That link has expired or is no longer available. Download it again from the title's page."
                        : $"The server answered {code}.";
                    Post(() => (job.State, job.Message, job.Resumable) = (DownloadState.Failed, message, false));
                    return;
                }

                var partial = response.StatusCode == System.Net.HttpStatusCode.PartialContent && from > 0;
                if (!partial) from = 0; // the server sent the whole file again
                var total = partial ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength;
                if (total is > 0) job.Total = total;
                Post(Save);

                keepHalf = true; // from here on, a break leaves the half file for Resume
                await using (var body = await response.Content.ReadAsStreamAsync(job.Stop.Token).ConfigureAwait(false))
                await using (var file = new FileStream(part, partial ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    var done = from;
                    var reported = Environment.TickCount64;
                    int count;
                    while ((count = await body.ReadAsync(buffer, job.Stop.Token).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, count), job.Stop.Token).ConfigureAwait(false);
                        done += count;
                        if (job.Total is > 0 && Environment.TickCount64 - reported > 200)
                        {
                            reported = Environment.TickCount64;
                            var fraction = Math.Min(1.0, (double)done / job.Total.Value);
                            Post(() => job.Fraction = fraction);
                        }
                    }
                }
            }

            File.Move(part, destination, true);
            keepHalf = false;
            Post(() =>
            {
                job.FilePath = destination;
                job.Fraction = 1;
                job.State = DownloadState.Done;
                Save();
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled by the person (the half file is deleted by Cancel)
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            var message = ex is HttpRequestException ? "The download stopped. Check your connection, then Resume." : "Couldn't save the file to that folder.";
            var resumable = keepHalf && ex is not UnauthorizedAccessException;
            Post(() =>
            {
                (job.State, job.Message, job.Resumable) = (DownloadState.Failed, message, resumable);
                Save();
            });
        }
        finally
        {
            try
            {
                // the half file goes when the download failed for good or was cancelled (by now the writer has let go of it)
                if ((!keepHalf || job.Stop.IsCancellationRequested) && part is not null && File.Exists(part)) File.Delete(part);
            }
            catch (IOException)
            {
                // left for the next clean-up
            }
        }
    }
}
