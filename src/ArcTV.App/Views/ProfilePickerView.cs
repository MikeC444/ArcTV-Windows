using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArcTV.App.Controls;
using ArcTV.Core.Account;
using ArcTV.Core.Profiles;

namespace ArcTV.App.Views;

/// <summary>
/// "Who's watching?": the account's profiles as pictures to choose from (a profile with a PIN asks for it first), and "Manage Profiles" to add one, change a name, picture, kids setting or
/// PIN, or remove one. Opening a profile hands it to <c>open</c>; the app then starts over on that profile's library.
/// </summary>
public sealed class ProfilePickerView : UserControl
{
    private static readonly Dictionary<string, ImageSource> Pictures = [];

    private readonly ProfilesModel _model;
    private readonly string? _currentId;
    private readonly Action<Profile> _open;
    private readonly Action? _close;
    private readonly ContentControl _body = new();
    private bool _manage;
    private bool _busy;

    public ProfilePickerView(ProfilesModel model, string? currentId, Action<Profile> open, Action? close = null)
    {
        _model = model;
        _currentId = currentId;
        _open = open;
        _close = close;
        Background = Res<Brush>("BgBrush");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Grid { MinHeight = 560, Children = { _body } } };
        _body.HorizontalAlignment = HorizontalAlignment.Center;
        _body.VerticalAlignment = VerticalAlignment.Center;
        _body.Margin = new Thickness(40);
        ShowList();
    }

    /// <summary>What the page is showing (for the tests): the profile cards, the PIN question or an editor.</summary>
    public string State { get; private set; } = "";

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    /// <summary>A profile's picture, drawn round.</summary>
    public static FrameworkElement Avatar(string id, double size)
    {
        var name = Avatars.Resolve(id);
        if (!Pictures.TryGetValue(name, out var source))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"pack://application:,,,/ArcTV;component/Assets/avatars/{name}.png");
            bitmap.DecodePixelWidth = 240;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            Pictures[name] = source = bitmap;
        }

        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = new ImageBrush(source) { Stretch = Stretch.UniformToFill },
        };
    }

    private static TextBlock Text(string text, double size, FontWeight? weight = null, Brush? brush = null, TextAlignment align = TextAlignment.Center) =>
        new() { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, Foreground = brush ?? Res<Brush>("TextBrush"), TextAlignment = align, TextWrapping = TextWrapping.Wrap };

    // ---- who's watching ----

    private void ShowList()
    {
        State = "list";
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(Text(_manage ? "Manage Profiles" : "Who's watching?", 44, FontWeights.ExtraBold));
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 36, 0, 28), MaxWidth = 1100 };
        foreach (var profile in _model.Profiles) wrap.Children.Add(Card(profile));
        if (_manage && _model.CanAdd) wrap.Children.Add(AddCard());
        stack.Children.Add(wrap);

        var manage = new Button { Content = _manage ? "Done" : "Manage Profiles", Style = Res<Style>("GhostButton"), Height = 46, Padding = new Thickness(30, 0, 30, 0), HorizontalAlignment = HorizontalAlignment.Center };
        manage.Click += (_, _) =>
        {
            _manage = !_manage;
            ShowList();
        };
        stack.Children.Add(manage);
        if (_close is not null && !_manage)
        {
            var back = new Button { Content = "Back", Style = Res<Style>("LinkButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            back.Click += (_, _) => _close();
            stack.Children.Add(back);
        }

        _body.Content = stack;
    }

    private Button Card(Profile profile)
    {
        var picture = new Grid { Width = 156, Height = 156, HorizontalAlignment = HorizontalAlignment.Center };
        var current = profile.Id == _currentId && !_manage;
        picture.Children.Add(new Border { CornerRadius = new CornerRadius(78), BorderThickness = new Thickness(3), BorderBrush = current ? Res<Brush>("AccentBrush") : new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)), Child = Avatar(profile.Avatar, 150) });
        if (_manage) picture.Children.Add(Badge("")); // a pencil
        else if (profile.HasPin) picture.Children.Add(Badge("")); // a padlock

        var column = new StackPanel { Width = 190, Margin = new Thickness(8, 6, 8, 6), Background = Brushes.Transparent };
        column.Children.Add(picture);
        column.Children.Add(Text(profile.Name, 18, FontWeights.SemiBold));
        if (profile.IsKids) column.Children.Add(new Border { CornerRadius = new CornerRadius(8), Background = Res<Brush>("PillGradient"), Padding = new Thickness(10, 1, 10, 2), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Child = Text("Kids", 11, FontWeights.Bold, Brushes.Black) });
        HoverLift.Attach(column, 1.07);

        var button = new Button { Style = Res<Style>("PosterButton"), Content = column, ToolTip = _manage ? $"Edit {profile.Name}" : $"Watch as {profile.Name}", Tag = profile.Id };
        button.Click += (_, _) =>
        {
            if (_manage) ShowEditor(profile);
            else if (profile.HasPin) ShowPin(profile);
            else _open(profile);
        };
        return button;
    }

    private Button AddCard()
    {
        var circle = new Border
        {
            Width = 150, Height = 150, CornerRadius = new CornerRadius(75), Background = Res<Brush>("SurfaceBrush"), BorderThickness = new Thickness(3), BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 255, 255, 255)),
            Child = new TextBlock { Text = "+", FontSize = 64, FontWeight = FontWeights.Light, Foreground = Res<Brush>("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -8, 0, 0) },
        };
        var column = new StackPanel { Width = 190, Margin = new Thickness(8, 6, 8, 6), Background = Brushes.Transparent };
        column.Children.Add(new Grid { Width = 156, Height = 156, HorizontalAlignment = HorizontalAlignment.Center, Children = { circle } });
        column.Children.Add(Text("Add profile", 18, FontWeights.SemiBold));
        HoverLift.Attach(column, 1.07);
        var button = new Button { Style = Res<Style>("PosterButton"), Content = column, Tag = "add", ToolTip = "Add a profile" };
        button.Click += (_, _) => ShowEditor(null);
        return button;
    }

    private static Border Badge(string glyph) => new()
    {
        Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), BorderThickness = new Thickness(1),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 4, 4),
        Child = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 16, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    // ---- a profile with a PIN ----

    private void ShowPin(Profile profile)
    {
        State = "pin";
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Width = 360 };
        var picture = Avatar(profile.Avatar, 120);
        picture.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(picture);
        stack.Children.Add(Text(profile.Name, 26, FontWeights.ExtraBold).WithMargin(0, 14, 0, 4));
        stack.Children.Add(Text("Enter this profile's 4-digit PIN", 14, brush: Res<Brush>("Text2Brush")));
        var box = new PasswordBox { Style = Res<Style>("PasswordField"), MaxLength = Profile.PinLength, FontSize = 28, HorizontalContentAlignment = HorizontalAlignment.Center, Width = 200, Margin = new Thickness(0, 18, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(box);
        var error = Text("", 13, brush: new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
        error.MinHeight = 20;
        stack.Children.Add(error);

        var go = new Button { Content = "Continue", Style = Res<Style>("PrimaryButton"), Height = 46, Margin = new Thickness(0, 10, 0, 0) };
        async void Submit()
        {
            if (_busy) return;
            _busy = true;
            go.IsEnabled = false;
            var problem = await _model.CheckPinAsync(profile, box.Password);
            _busy = false;
            go.IsEnabled = true;
            if (problem is null)
            {
                _open(profile);
                return;
            }

            error.Text = problem;
            box.Clear();
            box.Focus();
        }

        go.Click += (_, _) => Submit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Submit();
        };
        stack.Children.Add(go);
        var cancel = new Button { Content = "Cancel", Style = Res<Style>("LinkButton"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
        cancel.Click += (_, _) => ShowList();
        stack.Children.Add(cancel);
        _body.Content = stack;
        box.Loaded += (_, _) => box.Focus();
    }

    // ---- adding and editing ----

    private void ShowEditor(Profile? profile)
    {
        State = profile is null ? "add" : "edit";
        var picked = profile is null ? Avatars.Ids[0] : Avatars.Resolve(profile.Avatar);
        var stack = new StackPanel { Width = 560 };
        stack.Children.Add(Text(profile is null ? "Add profile" : "Edit profile", 34, FontWeights.ExtraBold).WithMargin(0, 0, 0, 20));

        var preview = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16), Content = Avatar(picked, 110) };
        stack.Children.Add(preview);

        stack.Children.Add(Label("Name"));
        var name = new TextBox { Style = Res<Style>("Field"), MaxLength = Profile.NameMax, Text = profile?.Name ?? "" };
        stack.Children.Add(name);

        stack.Children.Add(Label("Picture").WithMargin(0, 16, 0, 6));
        var grid = new WrapPanel();
        var chips = new Dictionary<string, Border>();
        foreach (var id in Avatars.Ids)
        {
            var ring = new Border { CornerRadius = new CornerRadius(30), BorderThickness = new Thickness(3), Margin = new Thickness(0, 0, 8, 8), Child = Avatar(id, 54), BorderBrush = Brushes.Transparent };
            var pick = new Button { Style = Res<Style>("PosterButton"), Content = ring, Tag = id, ToolTip = id };
            pick.Click += (_, _) =>
            {
                picked = id;
                preview.Content = Avatar(id, 110);
                foreach (var (key, chip) in chips) chip.BorderBrush = key == picked ? Res<Brush>("AccentBrush") : Brushes.Transparent;
            };
            chips[id] = ring;
            grid.Children.Add(pick);
        }

        chips[picked].BorderBrush = Res<Brush>("AccentBrush");
        stack.Children.Add(grid);

        CheckBox? kids = null;
        if (profile is not { IsDefault: true })
        {
            kids = new CheckBox { Content = "Kids profile (hides mature genres)", IsChecked = profile?.IsKids == true, Foreground = Res<Brush>("TextBrush"), FontSize = 14, Margin = new Thickness(0, 12, 0, 0) };
            stack.Children.Add(kids);
        }

        PasswordBox? current = null;
        CheckBox? remove = null;
        if (profile is { HasPin: true })
        {
            stack.Children.Add(Label("Current PIN (needed to change anything on this profile)").WithMargin(0, 16, 0, 6));
            stack.Children.Add(current = new PasswordBox { Style = Res<Style>("PasswordField"), MaxLength = Profile.PinLength, Width = 160, HorizontalAlignment = HorizontalAlignment.Left });
        }

        stack.Children.Add(Label(profile is { HasPin: true } ? "New PIN (leave empty to keep the current one)" : "PIN (optional: 4 digits, asked each time this profile is opened)").WithMargin(0, 16, 0, 6));
        var pin = new PasswordBox { Style = Res<Style>("PasswordField"), MaxLength = Profile.PinLength, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(pin);
        if (profile is { HasPin: true })
        {
            remove = new CheckBox { Content = "Remove the PIN", Foreground = Res<Brush>("TextBrush"), FontSize = 14, Margin = new Thickness(0, 10, 0, 0) };
            stack.Children.Add(remove);
        }

        var error = Text("", 13, brush: new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)), align: TextAlignment.Left);
        error.Margin = new Thickness(0, 14, 0, 0);
        stack.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var save = new Button { Content = "Save", Style = Res<Style>("PrimaryButton"), Height = 46, Padding = new Thickness(34, 0, 34, 0), Margin = new Thickness(0, 0, 12, 0) };
        var cancel = new Button { Content = "Cancel", Style = Res<Style>("GhostButton"), Height = 46, Padding = new Thickness(28, 0, 28, 0), Margin = new Thickness(0, 0, 12, 0) };
        cancel.Click += (_, _) => ShowList();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        async Task Run(Func<Task> work)
        {
            if (_busy) return;
            _busy = true;
            save.IsEnabled = false;
            try
            {
                await work();
                ShowList();
            }
            catch (Exception ex)
            {
                error.Text = AccountModel.Describe(ex);
                save.IsEnabled = true;
            }
            finally
            {
                _busy = false;
            }
        }

        save.Click += async (_, _) =>
        {
            if (ProfilesModel.NameProblem(name.Text) is { } nameProblem)
            {
                error.Text = nameProblem;
                return;
            }

            if (profile is { HasPin: true } && !Profile.IsValidPin(current?.Password))
            {
                error.Text = "Enter the current PIN.";
                return;
            }

            if (pin.Password.Length > 0 && !Profile.IsValidPin(pin.Password))
            {
                error.Text = "A PIN is exactly 4 digits.";
                return;
            }

            var isKids = kids?.IsChecked == true;
            await Run(() => profile is null
                ? _model.CreateAsync(name.Text, picked, isKids, pin.Password.Length > 0 ? pin.Password : null)
                : _model.UpdateAsync(profile, name.Text, picked, isKids,
                    remove?.IsChecked == true ? PinChange.Remove : pin.Password.Length > 0 ? PinChange.Set(pin.Password) : PinChange.Keep, current?.Password));
        };

        if (profile is { IsDefault: false })
        {
            var delete = new Button { Content = "Delete profile", Style = Res<Style>("GhostButton"), Height = 46, Padding = new Thickness(24, 0, 24, 0), Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)) };
            delete.Click += async (_, _) =>
            {
                if (delete.Tag is not "sure")
                {
                    delete.Tag = "sure";
                    delete.Content = "Really delete? Its list and ratings go too";
                    return;
                }

                if (profile.HasPin && !Profile.IsValidPin(current?.Password))
                {
                    error.Text = "Enter the current PIN.";
                    return;
                }

                await Run(() => _model.DeleteAsync(profile, current?.Password));
            };
            buttons.Children.Add(delete);
        }

        stack.Children.Add(buttons);
        _body.Content = stack;
        name.Loaded += (_, _) => name.Focus();
    }

    private static TextBlock Label(string text) => new() { Text = text, Style = Res<Style>("FieldLabel") };
}

internal static class PickerExtensions
{
    public static T WithMargin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }
}
