using CommunityToolkit.Mvvm.ComponentModel;

namespace Kronos.UserControls;

public partial class DLLRecordInfoViewModel : ObservableObject
{
    public DLLRecordInfoViewModelTranslationProperties TranslationProperties { get; } = new DLLRecordInfoViewModelTranslationProperties();

    public DLLRecordInfoViewModel()
    {

    }
}
