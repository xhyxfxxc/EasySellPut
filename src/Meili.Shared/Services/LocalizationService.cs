using System.Globalization;
using System.Xml.Linq;

namespace Meili.Shared.Services;

/// <summary>
/// zh-CN / en-US text for view-models on every platform. The strings live once, in the WPF string dictionaries
/// (Resources/Strings/Strings.*.xaml), embedded here and read as key/value pairs; each front end also exposes them to
/// its own XAML (WPF swaps a merged ResourceDictionary in <see cref="Applying"/>, Avalonia fills one from <see cref="Strings"/>).
/// </summary>
public sealed class LocalizationService
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = [];

    /// <summary>The instance created by the composition root (static access for row objects built outside DI).</summary>
    public static LocalizationService Current { get; private set; } = null!;

    public LocalizationService()
    {
        Strings = Load(Language);
        Current = this;
    }

    public string Language { get; private set; } = "zh-CN";
    public IReadOnlyDictionary<string, string> Strings { get; private set; }

    /// <summary>Raised with the new language before <see cref="LanguageChanged"/>, so the UI's own text resources are swapped first.</summary>
    public event Action<string>? Applying;
    public event EventHandler? LanguageChanged;

    /// <summary>G2: true for the Chinese UI (万/亿 amounts).</summary>
    public bool IsChinese => Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>G2: volume / amount in the current language's units.</summary>
    public string Amount(double? value, string empty = Meili.Core.Models.NumberDisplay.Empty) =>
        Meili.Core.Models.NumberDisplay.Amount(value, IsChinese, empty);

    public void Apply(string language)
    {
        if (language is not ("zh-CN" or "en-US")) language = "zh-CN";
        Strings = Load(language);
        Language = language;
        Applying?.Invoke(language);
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string this[string key] => Get(key);

    public string Get(string key) => Strings.TryGetValue(key, out var v) ? v : key;

    /// <summary>All keys and values of one language (also used by tests and the macOS resource dictionary).</summary>
    public static IReadOnlyDictionary<string, string> Load(string language)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(language, out var hit)) return hit;
            using var stream = typeof(LocalizationService).Assembly.GetManifestResourceStream($"Strings.{language}.xaml")!;
            var dict = XDocument.Load(stream).Root!.Elements()
                .Where(e => e.Attribute(X + "Key") is not null)
                .ToDictionary(e => e.Attribute(X + "Key")!.Value, e => e.Value);
            Cache[language] = dict;
            return dict;
        }
    }
}
