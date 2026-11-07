using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace EarthModelStudio.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this("") { }

    public ConfirmWindow(string message)
    {
        AvaloniaXamlLoader.Load(this);
        var text = this.FindControl<TextBlock>("MessageText");
        if (text != null) text.Text = message;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
