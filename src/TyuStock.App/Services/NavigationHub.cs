namespace TyuStock.App.Services;

public sealed record NavigationRequest(Type PageType, string? Symbol = null);

/// <summary>Lets view-models and toast clicks ask the shell to change page without referencing MainWindow.</summary>
public sealed class NavigationHub
{
    public event EventHandler<NavigationRequest>? Requested;

    public void Go(Type pageType, string? symbol = null) =>
        Requested?.Invoke(this, new NavigationRequest(pageType, symbol));
}