using Avalonia;
using Avalonia.Controls;

namespace Meili.Mac.Platform;

/// <summary>Exposes the UI text to XAML ({DynamicResource Key}); a language switch swaps one merged dictionary.</summary>
public static class MacStrings
{
    public static LocalizationService Attach(LocalizationService loc)
    {
        ResourceDictionary? current = null;
        loc.Applying += _ =>
        {
            var dict = new ResourceDictionary();
            foreach (var (key, value) in loc.Strings) dict[key] = value;
            var merged = Application.Current!.Resources.MergedDictionaries;
            if (current is not null) merged.Remove(current);
            merged.Add(dict);
            current = dict;
        };
        return loc;
    }
}
