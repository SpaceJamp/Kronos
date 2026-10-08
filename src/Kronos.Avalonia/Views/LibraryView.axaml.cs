using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kronos.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}