using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.Pages;

public class AcknowledgementsPageModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string LicencesAndAcknowledgementsText => ResourceHelper.GetString("AcknowledgementsPage_Title");
}
