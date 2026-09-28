using Microsoft.UI.Xaml.Controls;

namespace Kronos.UserControls;

public sealed partial class GameFilterControl : UserControl
{
    private GameFilterControlViewModel ViewModel { get; }

    public GameFilterControl()
    {
        this.InitializeComponent();
        ViewModel = new GameFilterControlViewModel();
        DataContext = ViewModel;
    }
}
