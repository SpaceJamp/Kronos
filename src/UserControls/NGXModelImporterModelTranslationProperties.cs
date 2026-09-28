using Kronos.Attributes;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos.UserControls;

public class NGXModelImporterModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string ColumnHeaderAssetTypeText => ResourceHelper.GetString("GameHistoryControl_AssetTypeHeader");

    [TranslationProperty]
    public string ColumnHeaderVersionText => ResourceHelper.GetString("General_Version");

    [TranslationProperty]
    public string ColumnHeaderSizeText => ResourceHelper.GetString("General_Size");

    [TranslationProperty]
    public string ColumnHeaderStatusText => ResourceHelper.GetString("General_Status");

    
}
