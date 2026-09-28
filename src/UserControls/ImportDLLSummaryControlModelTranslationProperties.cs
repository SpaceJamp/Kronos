using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.UserControls;
internal class ImportDLLSummaryControlModelTranslationProperties : LocalizedViewModelBase
{
    //public ImportDLLSummaryControlModelTranslationProperties() : base() { }

    [TranslationProperty]
    public string SuccessText => $"{ResourceHelper.GetString("General_Success")}: ";

    [TranslationProperty]
    public string FailedText => $"{ResourceHelper.GetString("General_Failed")}: ";
}
