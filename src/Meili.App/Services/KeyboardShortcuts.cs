using System.Windows.Input;
using Meili.App.Views.Pages;

namespace Meili.App.Services;

/// <summary>D2: the app's keyboard map, kept free of window code so it can be tested.</summary>
public static class KeyboardShortcuts
{
    /// <summary>Ctrl+1…9 in navigation order.</summary>
    public static readonly IReadOnlyList<Type> PageOrder =
    [
        typeof(DashboardPage), typeof(WatchlistPage), typeof(ChartsPage), typeof(OptionsPage), typeof(StrategyPage),
        typeof(NewsPage), typeof(AlertsPage), typeof(DataSourcesPage), typeof(SettingsPage),
    ];

    /// <summary>Page for Ctrl+digit (main row or numpad), or null.</summary>
    public static Type? PageFor(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & ~ModifierKeys.Shift) != ModifierKeys.Control) return null;
        int n = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad1 + 1,
            _ => 0,
        };
        return n is >= 1 and <= 9 ? PageOrder[n - 1] : null;
    }

    /// <summary>
    /// Whether typed text should open the keyboard wizard: a letter, digit, '^' or '.' typed while focus is not in a text input.
    /// </summary>
    public static bool StartsWizard(string? text, bool focusInTextInput)
    {
        if (focusInTextInput || string.IsNullOrEmpty(text) || text.Length != 1) return false;
        var c = text[0];
        return char.IsAsciiLetterOrDigit(c) || c is '^' or '.';
    }

    /// <summary>The page view-model's refresh command (F5), if it has one.</summary>
    public static ICommand? RefreshCommandOf(object? viewModel)
    {
        if (viewModel is null) return null;
        foreach (var name in new[] { "RefreshCommand", "RecheckCommand" })
            if (viewModel.GetType().GetProperty(name)?.GetValue(viewModel) is ICommand c) return c;
        return null;
    }
}
