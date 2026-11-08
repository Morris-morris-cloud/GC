using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace EarthModelStudio.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this("") { }

    public ConfirmWindow(string message)
    {
        AvaloniaXamlLoader.Load(this);

        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        if (this.Content is Control content)
        {
            this.Content = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = (this.FindResource("Surface") as IBrush) ?? Brushes.White,
                ClipToBounds = true,
                Child = content
            };
        }

        var text = this.FindControl<TextBlock>("MessageText");
        if (text != null) text.Text = message;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
