using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArcTV.App.Controls;

/// <summary>An image loaded from a web address, decoded at the size it is shown (posters are small, so rows of hundreds stay light). Shows nothing if it fails.</summary>
public sealed class RemoteImage : Image
{
    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string), typeof(RemoteImage), new PropertyMetadata(null, (d, _) => ((RemoteImage)d).Load()));

    public static readonly DependencyProperty DecodeWidthProperty = DependencyProperty.Register(nameof(DecodeWidth), typeof(int), typeof(RemoteImage), new PropertyMetadata(320));

    public string? Url { get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }

    public int DecodeWidth { get => (int)GetValue(DecodeWidthProperty); set => SetValue(DecodeWidthProperty, value); }

    public RemoteImage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    private void Load()
    {
        Source = null;
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = DecodeWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            if (image.IsDownloading)
            {
                image.DownloadCompleted += (_, _) => Source = image;
                image.DownloadFailed += (_, _) => Source = null;
            }
            else
            {
                Source = image;
            }
        }
        catch (Exception)
        {
            Source = null;
        }
    }
}
