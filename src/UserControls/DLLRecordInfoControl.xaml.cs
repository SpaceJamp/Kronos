using Chronos.Data;
using Microsoft.UI.Xaml.Controls;

namespace Chronos.UserControls;

public sealed partial class DLLRecordInfoControl : UserControl
{
    public DLLRecord DLLRecord { get; private set; }

    public DLLRecordInfoControl(DLLRecord dllRecord)
    {
        this.InitializeComponent();
        this.DLLRecord = dllRecord;
    }
}
