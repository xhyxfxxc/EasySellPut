namespace Meili.Shared.Services;

public sealed record NavigationRequest(AppPage Page, string? Symbol = null);

/// <summary>Lets view-models and notification clicks ask the shell to change page without referencing the window.</summary>
public sealed class NavigationHub
{
    public event EventHandler<NavigationRequest>? Requested;

    public void Go(AppPage page, string? symbol = null) =>
        Requested?.Invoke(this, new NavigationRequest(page, symbol));
}
