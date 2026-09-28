using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Chronos.Helpers;

namespace Chronos.Interfaces;

public abstract class LocalizedViewModelBase : ObservableObject, IDisposable
{
    public LocalizedViewModelBase()
    {
        LanguageManager.Instance.OnLanguageChanged += OnLanguageChanged;
    }

    protected virtual void OnLanguageChanged()
    {
        var currentClassType = GetType();
        var languageProperties = LanguageManager.GetClassLanguagePropertyNames(currentClassType);
        foreach (var propertyName in languageProperties)
        {
            OnPropertyChanged(propertyName);
        }
    }

    ~LocalizedViewModelBase()
    {
        Dispose();
    }

    public void Dispose()
    {
        LanguageManager.Instance.OnLanguageChanged -= OnLanguageChanged;
    }
}
