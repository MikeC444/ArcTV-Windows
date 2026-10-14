using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArcTV.Core.Downloads;
using ArcTV.Core.Storage;

namespace ArcTV.App.Views;

/// <summary>
/// The Downloads page (from the profile menu, or "See all downloads" in the Download panel): every file being saved, stopped or saved, with where they go. A download that was running when the
/// app closed is listed as stopped and can be resumed. Downloads are part of Arc TV Plus, so the page is simply empty without it.
/// </summary>
public sealed class DownloadsView : ScrollViewer
{
    private const string Mdl2 = "Segoe MDL2 Assets";
    private static readonly SolidColorBrush Amber = new(Color.FromRgb(0xFF, 0xB0, 0x20));
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x4A, 0xDE, 0x80));

    private readonly DownloadManager _manager;
    private readonly LocalSettings _local;
    private readonly StackPanel _list = new();
    private readonly TextBlock _folder = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Res("TextBrush") };
    private readonly Dictionary<DownloadJob, ProgressBar> _bars = [];

    public DownloadsView(DownloadManager manager, LocalSettings local)
    {
        _manager = manager;
        _local = local;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        manager.Folder = local.DownloadFolder ?? DownloadManager.DefaultFolder();
        _folder.Text = manager.Folder;

        var page = new StackPanel { Margin = new Thickness(24, 124, 24, 40), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(new TextBlock { Text = "Downloads", FontSize = 34, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(0, 0, 0, 12) });
        page.Children.Add(FolderRow());
        page.Children.Add(_list);
        Content = page;

        Loaded += (_, _) =>
        {
            _manager.Jobs.CollectionChanged += OnJobsChanged;
            foreach (var job in _manager.Jobs) job.PropertyChanged += OnJobChanged;
            Rebuild();
        };
        Unloaded += (_, _) =>
        {
            _manager.Jobs.CollectionChanged -= OnJobsChanged;
            foreach (var job in _manager.Jobs) job.PropertyChanged -= OnJobChanged;
        };
    }

    private UIElement FolderRow()
    {
        var change = new Button { Content = "Change", Style = Ui.StyleOf("ChoicePill"), Height = 34, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0), ToolTip = "Choose another folder to save downloads in" };
        change.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where downloads are saved", InitialDirectory = _manager.Folder };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true || string.IsNullOrEmpty(dialog.FolderName)) return;
            _local.DownloadFolder = dialog.FolderName;
            _manager.Folder = dialog.FolderName;
            _folder.Text = dialog.FolderName;
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        DockPanel.SetDock(change, Dock.Right);
        row.Children.Add(change);
        row.Children.Add(new TextBlock { Text = "Save to", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        row.Children.Add(_folder);
        return row;
    }

    private void OnJobsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (DownloadJob job in e.OldItems) job.PropertyChanged -= OnJobChanged;
        if (e.NewItems is not null)
            foreach (DownloadJob job in e.NewItems) job.PropertyChanged += OnJobChanged;
        Rebuild();
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DownloadJob job) return;
        if (e.PropertyName != nameof(DownloadJob.Fraction)) Rebuild();
        else if (_bars.TryGetValue(job, out var bar))
        {
            bar.IsIndeterminate = job.Fraction is null;
            bar.Value = job.Fraction ?? 0;
        }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        _bars.Clear();
        if (_manager.Jobs.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Nothing downloaded yet. Use the Download button on a movie or episode (Arc TV Plus) and it will show up here.", FontSize = 15, TextWrapping = WrapMode, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        var clear = new Button { Content = "Clear finished", Style = Ui.StyleOf("LinkButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 8) };
        clear.Click += (_, _) => _manager.ClearFinished();
        _list.Children.Add(clear);
        foreach (var job in _manager.Jobs) _list.Children.Add(Row(job));
    }

    private const TextWrapping WrapMode = TextWrapping.Wrap;

    private UIElement Row(DownloadJob job)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void Add(string text, Action action, bool primary = false)
        {
            var button = new Button { Content = text, Style = Ui.StyleOf(primary ? "PrimaryButton" : "ChoicePill"), Height = 36, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(8, 0, 0, 0) };
            button.Click += (_, _) => action();
            buttons.Children.Add(button);
        }

        switch (job.State)
        {
            case DownloadState.Running:
                Add("Cancel", () => _manager.Cancel(job));
                break;
            case DownloadState.Done:
                Add("Play", () => Open(job.FilePath), true);
                Add("Show in folder", () => ShowInFolder(job.FilePath));
                Add("Remove", () => _manager.Remove(job));
                break;
            default: // stopped or failed
                if (job.CanResume) Add("Resume", () => _manager.Resume(job), true);
                Add("Remove", () => _manager.Remove(job));
                break;
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = job.Name, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        switch (job.State)
        {
            case DownloadState.Running:
                var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Value = job.Fraction ?? 0, IsIndeterminate = job.Fraction is null, Foreground = Ui.Res("AccentBrush"), Background = Ui.Res("BgBrush"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) };
                _bars[job] = bar;
                text.Children.Add(bar);
                break;
            case DownloadState.Done:
                text.Children.Add(Line($"Saved to {(job.FilePath is { } saved ? Path.GetDirectoryName(saved) : _manager.Folder)}", Green));
                break;
            default:
                if (job.Fraction is { } part) text.Children.Add(new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Value = part, Foreground = Amber, Background = Ui.Res("BgBrush"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) });
                text.Children.Add(Line(job.Message ?? (job.State == DownloadState.Paused ? "Stopped. Resume to carry on." : "The download failed."), Amber));
                break;
        }

        var glyph = job.State switch { DownloadState.Done => "", DownloadState.Running => "", _ => "" };
        var icon = new Border
        {
            Width = 44, Height = 44, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, Background = Ui.Res("BgBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = glyph, FontFamily = new FontFamily(Mdl2), FontSize = 18, Foreground = Ui.Res("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };

        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        line.Children.Add(icon);
        line.Children.Add(buttons);
        line.Children.Add(text);
        return new Border { Child = line, Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new CornerRadius(14), Background = Ui.Res("SurfaceBrush"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
    }

    private static TextBlock Line(string text, Brush brush) => new() { Text = text, FontSize = 12, Foreground = brush, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

    private static void Open(string? path)
    {
        if (path is not null && File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static void ShowInFolder(string? path)
    {
        if (path is not null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }
}
