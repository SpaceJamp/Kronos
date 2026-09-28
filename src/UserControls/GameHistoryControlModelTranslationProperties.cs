using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;

public class GameHistoryControlModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string EventTimeHeader => ResourceHelper.GetString("GameHistoryControl_EventTimeHeader");

    [TranslationProperty]
    public string EventTypeHeader => ResourceHelper.GetString("GameHistoryControl_EventTypeHeader");

    [TranslationProperty]
    public string AssetTypeHeader => ResourceHelper.GetString("GameHistoryControl_AssetTypeHeader");

    [TranslationProperty]
    public string VersionHeader => ResourceHelper.GetString("GameHistoryControl_VersionHeader");

    [TranslationProperty]
    public string NoHistoryText => ResourceHelper.GetString("GameHistoryControl_NoHistoryLabel");
    
}
