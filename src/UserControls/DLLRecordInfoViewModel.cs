using CommunityToolkit.Mvvm.ComponentModel;

namespace Chronos.UserControls;

public partial class DLLRecordInfoViewModel : ObservableObject
{
    public DLLRecordInfoViewModelTranslationProperties TranslationProperties { get; } = new DLLRecordInfoViewModelTranslationProperties();

    public DLLRecordInfoViewModel()
    {

    }
}
