using Kronos.Attributes;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos.UserControls;
internal class ImportDLLSummaryControlModelTranslationProperties : LocalizedViewModelBase
{
    //public ImportDLLSummaryControlModelTranslationProperties() : base() { }

    [TranslationProperty]
    public string SuccessText => $"{ResourceHelper.GetString("General_Success")}: ";

    [TranslationProperty]
    public string FailedText => $"{ResourceHelper.GetString("General_Failed")}: ";
}
