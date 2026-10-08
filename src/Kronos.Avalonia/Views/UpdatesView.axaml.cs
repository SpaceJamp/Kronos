using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kronos.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}