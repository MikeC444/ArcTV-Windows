using System.Windows;
using System.Windows.Controls;
using ArcTV.Core.Account;

namespace ArcTV.App.Views;

/// <summary>Stands in for Home until it is built: shows who is signed in, with a way to sign out.</summary>
public sealed class SignedInPlaceholder : Grid
{
    public SignedInPlaceholder(AccountModel account)
    {
        var who = account.Session?.User;
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "You're signed in", FontSize = 40, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = who?.DisplayName is { Length: > 0 } name ? $"{name} · {who.Email}" : who?.Email ?? "", FontSize = 16, Margin = new Thickness(0, 10, 0, 6), HorizontalAlignment = HorizontalAlignment.Center, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text2Brush") });
        panel.Children.Add(new TextBlock { Text = "Home, Movies and TV Shows come next.", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text3Brush") });
        var out_ = new Button { Content = "Sign out", Width = 160, Margin = new Thickness(0, 28, 0, 0), Style = (Style)Application.Current.FindResource("GhostButton") };
        out_.Click += async (_, _) => await account.SignOutAsync();
        panel.Children.Add(out_);
        Children.Add(panel);
    }
}
