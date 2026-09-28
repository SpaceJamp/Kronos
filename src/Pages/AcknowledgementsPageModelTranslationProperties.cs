using Kronos.Attributes;
using Kronos.Helpers;
using Kronos.Interfaces;

namespace Kronos.Pages;

public class AcknowledgementsPageModelTranslationProperties : LocalizedViewModelBase
{
    [TranslationProperty]
    public string LicencesAndAcknowledgementsText => ResourceHelper.GetString("AcknowledgementsPage_Title");
}
