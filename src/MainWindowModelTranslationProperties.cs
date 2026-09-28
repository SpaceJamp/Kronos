using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos;

public class MainWindowModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string Title => ResourceHelper.GetString("ApplicationTitle");

    [TranslationProperty]
    public string AppTitleText => ResourceHelper.GetString("ApplicationTitle");

    [TranslationProperty]
    public string NavigationViewItemGamesText => ResourceHelper.GetString("GamesPage_Title");

    [TranslationProperty]
    public string NavigationViewItemLibraryText => ResourceHelper.GetString("LibraryPage_Title");

    [TranslationProperty]
    public string NavigationViewItemAcknowledgementsText => ResourceHelper.GetString("AcknowledgementsPage_Title");

    [TranslationProperty]
    public string LoadingProgressText => ResourceHelper.GetString("General_Loading");


}
