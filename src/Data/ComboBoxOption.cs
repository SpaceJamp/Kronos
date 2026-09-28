using Chronos.Attributes;
using Chronos.Helpers;
using Chronos.Interfaces;

namespace Chronos.Data;

public class ComboBoxOption : LocalizedViewModelBase
{
    public string LabelTranslationProperty { get; init; }

    [TranslationProperty]
    public string Label => ResourceHelper.GetString(LabelTranslationProperty);

    public int Value { get; init; }

    public ComboBoxOption(string labelLanguageProperty, int value)
    {
        LabelTranslationProperty = labelLanguageProperty;
        Value = value;
    }

    public override string ToString()
    {
        return Label;
    }
}
