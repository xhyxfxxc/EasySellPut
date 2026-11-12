using System.Globalization;
using Meili.Core.Alerts;

namespace Meili.Shared.Services;

/// <summary>Alert wording shared by the alert page and the Windows / macOS notifications.</summary>
public static class AlertText
{
    public static string Title(PriceAlert alert, LocalizationService loc) => string.Format(loc["Al_FiredToastTitle"], alert.Symbol);

    public static string Body(PriceAlert alert, AlertEvalResult result, LocalizationService loc)
    {
        var value = FormatValue(alert.Kind, result.Value ?? 0);
        var key = alert.Kind switch
        {
            AlertKind.PriceAbove => "Al_FiredPriceAbove",
            AlertKind.PriceBelow => "Al_FiredPriceBelow",
            AlertKind.PercentUp => "Al_FiredPercentUp",
            AlertKind.PercentDown => "Al_FiredPercentDown",
            AlertKind.VolumeAbove => "Al_FiredVolumeAbove",
            AlertKind.IvAbove => "Al_FiredIvAbove",
            AlertKind.IvBelow => "Al_FiredIvBelow",
            _ => "Al_FiredPriceAbove",
        };
        return string.Format(loc[key], value, FormatValue(alert.Kind, alert.Threshold));
    }

    public static string FormatValue(AlertKind kind, double value) => kind switch
    {
        AlertKind.PercentUp or AlertKind.PercentDown => value.ToString("0.##", CultureInfo.InvariantCulture),
        AlertKind.IvAbove or AlertKind.IvBelow => value.ToString("0.0", CultureInfo.InvariantCulture),
        AlertKind.VolumeAbove => value.ToString("N0", CultureInfo.InvariantCulture),
        _ => Meili.Core.Models.NumberDisplay.Price(value),
    };

    public static string IvBasisText(IvSample sample, LocalizationService loc)
    {
        var src = Meili.Shared.ViewModels.SourceNames.Display(sample.Source, loc);
        var text = sample.Basis == IvBasis.Iv30
            ? string.Format(loc["Al_IvBasisIv30"], src)
            : string.Format(loc["Al_IvBasisAtm"], src, sample.Expiration?.ToString("yyyy-MM-dd") ?? "—", sample.DaysToExpiry ?? 0);
        if (sample.AsOf is { } t) text += " · " + t.ToLocalTime().ToString("MM-dd HH:mm");
        return text;
    }
}
