using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GC.Views;

public partial class HomeView : UserControl
{
    public HomeView() => AvaloniaXamlLoader.Load(this);
}
