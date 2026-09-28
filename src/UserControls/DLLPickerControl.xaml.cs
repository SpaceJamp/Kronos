using Microsoft.UI.Xaml.Controls;
using Kronos.Data;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kronos.UserControls;

public sealed partial class DLLPickerControl : UserControl
{
    public DLLPickerControlModel ViewModel { get; private set; }

    public DLLPickerControl(GameControl gameControl, EasyContentDialog parentDialog, Game game, GameAssetType gameAssetType)
    {
        this.InitializeComponent();

        ViewModel = new DLLPickerControlModel(gameControl, parentDialog, this, game, gameAssetType);
        DataContext = ViewModel;
    }
}
