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
    private const int MaxAtOnce = 6;
    private const int Attempts = 3;
    private const int KeptPictures = 120;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Gate = new(MaxAtOnce);
    private static readonly Dictionary<string, BitmapSource> Kept = [];
    private static readonly Queue<string> KeptOrder = new();

    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string), typeof(RemoteImage), new PropertyMetadata(null, (d, _) => ((RemoteImage)d).Load()));

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(nameof(DecodeWidth), typeof(int), typeof(RemoteImage), new PropertyMetadata(320));

    private CancellationTokenSource? _cancel;

    public string? Url { get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }

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

    private async void Load()
    {
        _cancel?.Cancel();
        Source = null;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;

        var cancel = (_cancel = new CancellationTokenSource()).Token;
        var key = $"{DecodeWidth}|{uri}";
        if (Kept.TryGetValue(key, out var hit))
        {
            Source = hit;
            return;
        }

        var width = DecodeWidth;
        for (var attempt = 1; attempt <= Attempts && !cancel.IsCancellationRequested; attempt++)
        {
            try
            {
                var picture = await Task.Run(() => FetchAsync(uri, width, cancel), cancel);
                if (cancel.IsCancellationRequested) return;
                Keep(key, picture);
                Source = picture;
                return;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                if (attempt == Attempts) return;
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
            image.DecodePixelWidth = width;
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
