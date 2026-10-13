using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArcTV.App.Controls;

/// <summary>
/// An image loaded from a web address, decoded at the size it is shown (posters are small, so rows of hundreds stay light). A few pictures are fetched at a
/// time, a failed one is tried again a couple of times, and the last pictures shown are kept so scrolling back is instant. Shows nothing if it never loads.
/// </summary>
public sealed class RemoteImage : Image
{
    private const int MaxAtOnce = 8;
    private const int Attempts = 3;
    private const int KeptPictures = 120;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Gate = new(MaxAtOnce);
    private static readonly Dictionary<string, BitmapSource> Kept = [];
    private static readonly Queue<string> KeptOrder = new();

    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string), typeof(RemoteImage), new PropertyMetadata(null, (d, _) => ((RemoteImage)d).Schedule()));

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(nameof(DecodeWidth), typeof(int), typeof(RemoteImage), new PropertyMetadata(320, (d, _) => ((RemoteImage)d).Schedule()));

    public static readonly DependencyProperty FallbackUrlProperty = DependencyProperty.Register(nameof(FallbackUrl), typeof(string), typeof(RemoteImage), new PropertyMetadata(null, (d, _) => ((RemoteImage)d).Schedule()));

    /// <summary>Raised when a picture has loaded and is on show.</summary>
    public event EventHandler? Shown;

    private CancellationTokenSource? _cancel;
    private bool _scheduled;


    public string? Url { get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }

    /// <summary>Tried when <see cref="Url"/> is not there or never loads (for a bigger picture that may not exist for every title).</summary>
    public string? FallbackUrl { get => (string?)GetValue(FallbackUrlProperty); set => SetValue(FallbackUrlProperty, value); }

    public int DecodeWidth { get => (int)GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }

    public RemoteImage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Unloaded += (_, _) => _cancel?.Cancel();
        Loaded += (_, _) =>
        {
            if (Source is null && (_cancel is null || _cancel.IsCancellationRequested)) Load();
        };
    }

    /// <summary>
    /// Loading starts once the settings that go with a picture are all in (its address, a fallback address, the size to decode at), not at the first of them:
    /// they are set one after another, and starting at the address used the default size for the picture.
    /// </summary>
    private void Schedule()
    {
        if (_scheduled) return;
        _scheduled = true;
        Dispatcher.BeginInvoke(() =>
        {
            _scheduled = false;
            Load();
        });
    }

    private async void Load()
    {
        _cancel?.Cancel();
        Source = null;
        var addresses = new[] { Url, FallbackUrl }
            .Select(text => Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null)
            .OfType<Uri>()
            .Distinct()
            .ToList();
        if (addresses.Count == 0) return;

        var cancel = (_cancel = new CancellationTokenSource()).Token;
        var width = DecodeWidth;
        foreach (var uri in addresses)
        {
            var key = $"{width}|{uri}";
            if (Kept.TryGetValue(key, out var hit))
            {
                BeginAnimation(OpacityProperty, null);
                Source = hit;
                Shown?.Invoke(this, EventArgs.Empty);
                return;
            }

            for (var attempt = 1; attempt <= Attempts && !cancel.IsCancellationRequested; attempt++)
            {
                try
                {
                    var picture = await Task.Run(() => FetchAsync(uri, width, cancel), cancel);
                    if (cancel.IsCancellationRequested) return;
                    Keep(key, picture);
                    Source = picture;
                    BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                    Shown?.Invoke(this, EventArgs.Empty);
                    return;
                }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Gone)
                {
                    break; // not there: trying again will not help, so go on to the next address
                }
                catch (Exception)
                {
                    if (attempt == Attempts) break;
                    try
                    {
                        await Task.Delay(attempt * 1500, cancel);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
    }

    private static async Task<BitmapSource> FetchAsync(Uri uri, int width, CancellationToken cancel)
    {
        await Gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var bytes = await Http.GetByteArrayAsync(uri, cancel).ConfigureAwait(false);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            if (width > 0) image.DecodePixelWidth = width;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void Keep(string key, BitmapSource picture)
    {
        if (Kept.ContainsKey(key)) return;
        Kept[key] = picture;
        KeptOrder.Enqueue(key);
        while (KeptOrder.Count > KeptPictures) Kept.Remove(KeptOrder.Dequeue());
    }
}
