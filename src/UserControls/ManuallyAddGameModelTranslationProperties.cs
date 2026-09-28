using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;

public class ManuallyAddGameModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string AddCoverText => ResourceHelper.GetString("GamePage_AddCustomCover");

    [TranslationProperty]
    public string OptionalText => ResourceHelper.GetString("General_Optional");

    [TranslationProperty]
    public string NameText => ResourceHelper.GetString("General_Name");

    [TranslationProperty]
    public string InstallPathText => ResourceHelper.GetString("GamePage_InstallPath");
}
