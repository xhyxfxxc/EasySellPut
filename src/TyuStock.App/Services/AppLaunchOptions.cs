namespace TyuStock.App.Services;

/// <summary>
/// Process start flags. <c>--no-alerts</c> skips background monitoring and toasts (used for UI checks).
/// <c>--exit-on-close</c> makes the main window close quit the process instead of hiding to the tray.
/// </summary>
public sealed class AppLaunchOptions
{
    public bool NoAlerts { get; init; }
    public bool ExitOnClose { get; init; }

    public static AppLaunchOptions Parse(IReadOnlyList<string> args)
    {
        args ??= [];
        bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        return new AppLaunchOptions
        {
            NoAlerts = Has("--no-alerts") || Has("--no-notifications"),
            ExitOnClose = Has("--exit-on-close"),
        };
    }
}