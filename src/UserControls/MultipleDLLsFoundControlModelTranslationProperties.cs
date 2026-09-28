using Kronos.Attributes;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos.UserControls;

public class MultipleDLLsFoundControlModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string BelowMultipleDllFoundYouWillBeAbleToSwapInfo => ResourceHelper.GetString("GamePage_MultipleDllFound_Notice");

    [TranslationProperty]
    public string OpenDllLocationText => ResourceHelper.GetString("GamePage_OpenDllLocation");
}
