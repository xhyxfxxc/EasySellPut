using System.Globalization;
using System.Windows;

namespace Meili.App.Services;

/// <summary>Runtime zh-CN / en-US switching by swapping a merged ResourceDictionary (all UI text uses DynamicResource).</summary>
public sealed class LocalizationService
{
    /// <summary>B4: computed, so renaming the assembly cannot break the string dictionaries.</summary>
    private static readonly string AssemblyName = typeof(LocalizationService).Assembly.GetName().Name!;

    private ResourceDictionary? _current;
    public string Language { get; private set; } = "zh-CN";
    public event EventHandler? LanguageChanged;

    /// <summary>G2: true for the Chinese UI (万/亿 amounts).</summary>
    public bool IsChinese => Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>G2: volume / amount in the current language's units.</summary>
    public string Amount(double? value, string empty = Meili.Core.Models.NumberDisplay.Empty) =>
        Meili.Core.Models.NumberDisplay.Amount(value, IsChinese, empty);

    public void Apply(string language)
    {
        if (language is not ("zh-CN" or "en-US")) language = "zh-CN";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{AssemblyName};component/Resources/Strings/Strings.{language}.xaml", UriKind.Absolute),
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
