using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;

public class DLLPickerControlModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string NoDllsFoundText => ResourceHelper.GetString("GamePage_NoDllsFoundError_Title");

    [TranslationProperty]
    public string PleaseNavigateLibraryToDownloadDllsText => ResourceHelper.GetString("GamePage_NoDllsFoundError_Message");

    [TranslationProperty]
    public string OpenDllLocationText => ResourceHelper.GetString("GamePage_OpenDllLocation");

    [TranslationProperty]
    public string CurrentDllText => ResourceHelper.GetString("GamePage_CurrentDll");

    [TranslationProperty]
    public string OriginalDllRestoreText => ResourceHelper.GetString("GamePage_RestoreOriginalDll");

    [TranslationProperty]
    public string OriginalDllText => ResourceHelper.GetString("GamePage_OriginalDll");

    [TranslationProperty]
    public string ImportText => ResourceHelper.GetString("General_Import");
}
