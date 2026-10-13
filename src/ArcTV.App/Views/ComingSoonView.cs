using System.Windows;
using System.Windows.Controls;

namespace ArcTV.App.Views;

public sealed class ComingSoonView : Grid
{
    public ComingSoonView(string place)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = place, FontSize = 34, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "Coming soon to Arc TV for Windows.", FontSize = 15, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text2Brush") });
        Children.Add(panel);
    }
}
