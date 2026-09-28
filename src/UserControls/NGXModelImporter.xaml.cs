using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Kronos.Data.NVIDIA;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Kronos.UserControls;

public sealed partial class NGXModelImporter : UserControl
{
    public NGXModelImporterModel ViewModel { get; private set; }

    public NGXModelImporter(List<NGXModel> models)
    {
        InitializeComponent();

        ViewModel = new NGXModelImporterModel(this, models);
        DataContext = ViewModel;
    }
}
