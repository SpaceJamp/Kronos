using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chronos.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Chronos;

public partial class DiagnosticsWindowModel : ObservableObject
{
    public string DiagnosticsLog { get; set; } = string.Empty;

    public DiagnosticsWindowModelTranslationProperties TranslationProperties { get; } = new DiagnosticsWindowModelTranslationProperties();

    public DiagnosticsWindowModel() : base()
    {
        var systemDetails = new SystemDetails();
        DiagnosticsLog = $"{systemDetails.GetSystemData()}\n\n{systemDetails.GetLibraryData()}\n";
    }

    [RelayCommand]
    void CopyText()
    {
        var package = new DataPackage();
        package.SetText(DiagnosticsLog);
        Clipboard.SetContent(package);
    }
}
