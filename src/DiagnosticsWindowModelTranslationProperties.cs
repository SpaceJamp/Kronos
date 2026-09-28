using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos;

public class DiagnosticsWindowModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string ApplicationTilteDiagnosticsWindowText => $"{ResourceHelper.GetString("ApplicationTitle")} - {ResourceHelper.GetString("DiagnosticsPage_WindowTitle")}";

    [TranslationProperty]
    public string ClickToCopyDetailsText => ResourceHelper.GetString("DiagnosticsPage_ClickToCopyDetails");
}
