using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace EarthModelStudio.Views;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow() : this("") { }

    public ConfirmWindow(string message) : this(null, message, null, null) { }

    /// <summary>
    /// 可定制标题与按钮文案的确认窗。
    /// </summary>
    public ConfirmWindow(string? title, string message, string? confirmText, string? cancelText)
    {
        AvaloniaXamlLoader.Load(this);

        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        if (this.Content is Control content)
        {
            // 先把 content 从 Window 上摘下来，否则再赋给 Border.Child 会抛
            // "The Control already has a parent."
            this.Content = null;
            this.Content = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderBrush = (this.FindResource("Line") as IBrush) ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = (this.FindResource("Surface") as IBrush) ?? Brushes.White,
                ClipToBounds = true,
                Child = content
            };
        }

        var text = this.FindControl<TextBlock>("MessageText");
        if (text != null) text.Text = message;

        if (!string.IsNullOrWhiteSpace(title))
        {
            var titleBlock = this.FindControl<TextBlock>("TitleText");
            if (titleBlock != null) titleBlock.Text = title;
        }

        if (!string.IsNullOrWhiteSpace(confirmText))
        {
            var ok = this.FindControl<Button>("ConfirmButton");
            if (ok != null) ok.Content = confirmText;
        }

        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            // 取消按钮是第一个（"取消"），这里按名查找更稳
            var cancel = this.FindControl<Button>("CancelButton");
            if (cancel != null) cancel.Content = cancelText;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
}
