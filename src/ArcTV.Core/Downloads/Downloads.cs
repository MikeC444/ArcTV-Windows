using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using ArcTV.Core.Details;
using ArcTV.Core.Mvvm;
using ArcTV.Core.Sources;

namespace ArcTV.Core.Downloads;

/// <summary>Choosing and naming what to save from a title's page (an Arc TV Plus feature), as on the Mac app.</summary>
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

    internal DownloadJob(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }

    public string Name { get; }

    /// <summary>0 to 1, or null while the size is not known.</summary>
    public double? Fraction { get => _fraction; internal set => Set(ref _fraction, value); }

    public DownloadState State { get => _state; internal set => Set(ref _state, value); }

    /// <summary>Why it failed, in words.</summary>
    public string? Message { get => _message; internal set => Set(ref _message, value); }

    /// <summary>Where the finished file is.</summary>
    public string? FilePath { get => _filePath; internal set => Set(ref _filePath, value); }

    internal CancellationTokenSource Stop { get; } = new();
}

/// <summary>Saves files from a title's page into the PC's Downloads folder and keeps what each one is doing. A file is written as "name.part" and renamed when it is whole.</summary>
public sealed class DownloadManager(HttpClient http, string folder)
{
    private int _next;

    /// <summary>Where files are saved (the person can change it; a file already being saved keeps the folder it started in).</summary>
    public string Folder { get; set; } = folder;

    public ObservableCollection<DownloadJob> Jobs { get; } = [];

    /// <summary>The Downloads folder of the signed-in Windows user.</summary>
    public static string DefaultFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>Starts saving <paramref name="url"/> as <paramref name="fileName"/> (a number is added when that name is taken). Call it on the screen's thread: the job's changes are reported there.</summary>
    public DownloadJob Start(Uri url, string fileName)
    {
        var context = SynchronizationContext.Current;
        var job = new DownloadJob(Interlocked.Increment(ref _next), fileName);
        Jobs.Insert(0, job);
        _ = RunAsync(job, url, context);
        return job;
    }

    public void Cancel(DownloadJob job)
    {
        job.Stop.Cancel();
        Jobs.Remove(job);
    }

    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(job => job.State != DownloadState.Running).ToList()) Jobs.Remove(job);
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

    private async Task RunAsync(DownloadJob job, Uri url, SynchronizationContext? context)
    {
        void Post(Action change)
        {
            if (context is null) change();
            else context.Post(_ => change(), null);
        }

        string? part = null;
        try
        {
            var where = Folder;
            Directory.CreateDirectory(where);
            var destination = UniquePath(where, job.Name);
            part = destination + ".part";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, job.Stop.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Post(() => (job.State, job.Message) = (DownloadState.Failed, $"The server answered {(int)response.StatusCode}."));
                return;
            }

            var total = response.Content.Headers.ContentLength;
            await using (var body = await response.Content.ReadAsStreamAsync(job.Stop.Token).ConfigureAwait(false))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long done = 0;
                var reported = Environment.TickCount64;
                int count;
                while ((count = await body.ReadAsync(buffer, job.Stop.Token).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, count), job.Stop.Token).ConfigureAwait(false);
                    done += count;
                    if (total is > 0 && Environment.TickCount64 - reported > 200)
                    {
                        reported = Environment.TickCount64;
                        var fraction = Math.Min(1.0, (double)done / total.Value);
                        Post(() => job.Fraction = fraction);
                    }
                }
            }

            File.Move(part, destination);
            part = null;
            Post(() =>
            {
                job.FilePath = destination;
                job.Fraction = 1;
                job.State = DownloadState.Done;
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled by the person: the half file goes
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            var message = ex is HttpRequestException ? "The download stopped. Check your connection and try again." : "Couldn't save the file to that folder.";
            Post(() => (job.State, job.Message) = (DownloadState.Failed, message));
        }
        finally
        {
            try
            {
                if (part is not null && File.Exists(part)) File.Delete(part);
            }
            catch (IOException)
            {
                // left for the next clean-up
            }
        }
    }
}
