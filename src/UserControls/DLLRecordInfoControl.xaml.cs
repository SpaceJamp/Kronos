using Kronos.Data;
using Microsoft.UI.Xaml.Controls;

namespace Kronos.UserControls;

public sealed partial class DLLRecordInfoControl : UserControl
{
    public DLLRecord DLLRecord { get; private set; }

    public DLLRecordInfoControl(DLLRecord dllRecord)
    {
        this.InitializeComponent();
        this.DLLRecord = dllRecord;
    }
}
