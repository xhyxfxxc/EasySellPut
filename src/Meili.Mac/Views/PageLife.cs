using Avalonia;
using Avalonia.Controls;

namespace Meili.Mac.Views;

/// <summary>Page activation like the Windows Loaded/Unloaded pair: a page polls only while it is on screen.</summary>
internal static class PageLife
{
    public static void Bind(Control page, Func<Task> activate, Action deactivate)
    {
        page.AttachedToVisualTree += async (_, _) => await activate();
        page.DetachedFromVisualTree += (_, _) => deactivate();
    }
}

/// <summary>
/// Localized DataGrid column headers (<c>local:Loc.Header="Key"</c>): columns are not in the resource tree, so
/// {DynamicResource} cannot reach them; this follows a language switch instead.
/// </summary>
public sealed class Loc : AvaloniaObject
{
    public static readonly AttachedProperty<string?> HeaderProperty =
        AvaloniaProperty.RegisterAttached<Loc, DataGridColumn, string?>("Header");

    static Loc()
    {
        HeaderProperty.Changed.AddClassHandler<DataGridColumn>((column, e) =>
        {
            if (e.NewValue is not string key) return;
            var loc = LocalizationService.Current;
            column.Header = loc[key];
            loc.LanguageChanged += (_, _) => column.Header = loc[key];
        });
    }

    public static string? GetHeader(DataGridColumn column) => column.GetValue(HeaderProperty);
    public static void SetHeader(DataGridColumn column, string? value) => column.SetValue(HeaderProperty, value);
}
