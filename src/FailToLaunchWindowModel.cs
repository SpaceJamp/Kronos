using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Chronos.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace Chronos;

public partial class FailToLaunchWindowModel : ObservableObject
{
    public string SystemData { get; set; } = string.Empty;

    public FailToLaunchWindowModelTranslationProperties TranslationProperties { get; set; } = new FailToLaunchWindowModelTranslationProperties();

    public FailToLaunchWindowModel() : base()
    {
        var systemDetails = new SystemDetails();
        SystemData = systemDetails.GetSystemData();
    }

    [RelayCommand]
    void CopyText()
    {
        var package = new DataPackage();
        package.SetText(SystemData);
        Clipboard.SetContent(package);
    }
}
