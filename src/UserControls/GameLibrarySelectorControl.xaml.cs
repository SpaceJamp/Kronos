using Microsoft.UI.Xaml.Controls;

namespace Kronos.UserControls;

internal partial class GameLibrarySelectorControl : UserControl
{
    public GameLibrarySelectorControlModel ViewModel { get; private set; }

    public GameLibrarySelectorControl()
    {
        this.InitializeComponent();
        ViewModel = new GameLibrarySelectorControlModel();
        DataContext = ViewModel;
    }
}
