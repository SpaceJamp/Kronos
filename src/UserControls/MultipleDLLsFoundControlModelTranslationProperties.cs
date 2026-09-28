using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;

public class MultipleDLLsFoundControlModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string BelowMultipleDllFoundYouWillBeAbleToSwapInfo => ResourceHelper.GetString("GamePage_MultipleDllFound_Notice");

    [TranslationProperty]
    public string OpenDllLocationText => ResourceHelper.GetString("GamePage_OpenDllLocation");
}
