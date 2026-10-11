using Kronos.Attributes;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos;

public class CrashWindowModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string CrashWindowTitleText => $"{ResourceHelper.GetString("ApplicationTitle")} - {ResourceHelper.GetString("CrashWindow_WindowTitle")}";

    [TranslationProperty]
    public string CrashWindowExplanationText => ResourceHelper.GetString("CrashWindow_ExplanationText");

    [TranslationProperty]
    public string CopyDetailsText => ResourceHelper.GetString("DiagnosticsPage_ClickToCopyDetails");

    [TranslationProperty]
    public string OpenLogFolderText => ResourceHelper.GetString("CrashWindow_OpenLogFolder");

    [TranslationProperty]
    public string CopyLogPathText => ResourceHelper.GetString("CrashWindow_CopyLogPath");

    [TranslationProperty]
    public string CloseText => ResourceHelper.GetString("General_Close");
}