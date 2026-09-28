using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;

public class GameFilterControlViewModelTranslationProperties : LocalizedViewModelBase
{
    //public GameFilterControlViewModelTranslationProperties() : base() { }

    [TranslationProperty]
    public string OptionsText => $"{ResourceHelper.GetString("General_Options")}:";

    [TranslationProperty]
    public string GroupingText => $"{ResourceHelper.GetString("GamesPage_Grouping")}:";

    [TranslationProperty]
    public string HideGamesWithNoSwappableItemsText => ResourceHelper.GetString("GamesPage_HideGamesWithNoSwappableItems");

    [TranslationProperty]
    public string ShowHiddenGamesText => ResourceHelper.GetString("GamesPage_ShowHiddenGamesText");

    [TranslationProperty]
    public string GroupGamesFromTheSameLibraryTogetherText => ResourceHelper.GetString("GamesPage_GroupGamesFromTheSameLibraryTogether");
}
