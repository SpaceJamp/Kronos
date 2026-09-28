using Kronos.Attributes;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos;

public class DiagnosticsWindowModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string ApplicationTilteDiagnosticsWindowText => $"{ResourceHelper.GetString("ApplicationTitle")} - {ResourceHelper.GetString("DiagnosticsPage_WindowTitle")}";

    [TranslationProperty]
    public string ClickToCopyDetailsText => ResourceHelper.GetString("DiagnosticsPage_ClickToCopyDetails");
}
