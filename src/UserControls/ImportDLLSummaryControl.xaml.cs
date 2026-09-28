using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Chronos.Data;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Chronos.UserControls;

public sealed partial class ImportDLLSummaryControl : UserControl
{
    ImportDLLSummaryControlModel ViewModel { get; }

    public ImportDLLSummaryControl(IReadOnlyList<DLLImportResult> dllImportResults)
    {
        this.InitializeComponent();
        ViewModel = new ImportDLLSummaryControlModel(dllImportResults);
        DataContext = ViewModel;
    }
}
