using System.Globalization;
using System.Windows;

namespace TyuStock.App.Services;

/// <summary>Runtime zh-CN / en-US switching by swapping a merged ResourceDictionary (all UI text uses DynamicResource).</summary>
public sealed class LocalizationService
{
    private ResourceDictionary? _current;
    public string Language { get; private set; } = "zh-CN";
    public event EventHandler? LanguageChanged;

    public void Apply(string language)
    {
        if (language is not ("zh-CN" or "en-US")) language = "zh-CN";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/TyuStockApp;component/Resources/Strings/Strings.{language}.xaml", UriKind.Absolute),
        };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Language = language;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] => Get(key);

    public string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;
}
