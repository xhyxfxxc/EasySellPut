using Meili.Core.Diagnostics;
using System.IO;
using Meili.Core.Export;
using Meili.Core.Models;
using Meili.Core.Pricing;

namespace Meili.Shared.Services;

/// <summary>Save dialog for CSV (UTF-8 BOM) and XLSX. Returns a localized status string; never throws to the UI.</summary>
public static class ExportUi
{
    /// <summary>The dialog runs on the UI thread; the file is written on the thread pool so a large XLSX never stalls the UI.</summary>
    public static async Task<string?> SaveAsync(string suggestedName, IReadOnlyList<ExportTable> tables, LocalizationService loc)
    {
        if (tables.Count == 0 || tables.All(t => t.Rows.Count == 0))
            return loc["Ex_NoData"];
        var path = await Ui.Platform.PickSavePathAsync(loc["Ex_DialogTitle"], Sanitize(suggestedName));
        if (path is null) return null;
        var meta = (loc["Ex_MetaSheet"], loc["Ex_MetaTable"], loc["Ex_MetaItem"], loc["Ex_MetaValue"]);
        try
        {
            path = await Task.Run(() => Write(path, tables, meta));
            return string.Format(loc["Ex_Saved"], path);
        }
        catch (Exception ex)
        {
            AppLog.Error("export", "export failed", ex);
            return string.Format(loc["Ex_Failed"], ex.Message);
        }
    }

    private static string Write(string path, IReadOnlyList<ExportTable> tables, (string, string, string, string) meta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            if (tables.Count == 1)
                TableExporter.WriteCsv(tables[0], path);
            else
            {
                var dir = Path.GetDirectoryName(path)!;
                var stem = Path.GetFileNameWithoutExtension(path);
                foreach (var table in tables)
                    TableExporter.WriteCsv(table, Path.Combine(dir, $"{stem}_{Sanitize(table.Name)}.csv"));
            }
            return path;
        }
        if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            path += ".xlsx";
        TableExporter.WriteXlsx(tables, path, meta);
        return path;
    }

    /// <summary>
    /// F9: provenance rows for an export, from the same freshness model the UI uses. <paramref name="data"/> is the
    /// dataset the table came from (null for multi-source tables, which carry per-row source columns instead).
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Metadata(LocalizationService loc, SourcedData? data,
        RiskFreeRate? rate = null, string? note = null, DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.Now;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var list = new List<KeyValuePair<string, string>>
        {
            new(loc["Ex_MetaExported"], t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", inv)),
        };
        if (data is not null)
        {
            list.Add(new(loc["Ex_MetaSource"], ViewModels.SourceNames.Display(data.Source, loc)));
            list.Add(new(loc["Ex_MetaTiming"], FreshnessText.Timing(DataFreshness.Evaluate(data, t), loc)));
            list.Add(new(loc["Ex_MetaAsOf"], data.AsOf is { } a
                ? Meili.Core.Time.MarketClock.ToEastern(a).ToString("yyyy-MM-dd HH:mm:ss", inv)
                : loc["Ex_MetaUnknown"]));
            if (data.FetchedAt is { } f)
                list.Add(new(loc["Ex_MetaFetched"], f.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", inv)));
        }
        if (rate is not null) list.Add(new(loc["Ex_MetaRate"], PricingText.Model(rate, loc)));
        if (!string.IsNullOrWhiteSpace(note)) list.Add(new(loc["Ex_MetaNote"], note));
        return list;
    }

    public static IReadOnlyList<ExportTable> WithMetadata(IReadOnlyList<ExportTable> tables, IReadOnlyList<KeyValuePair<string, string>> meta)
        => tables.Select(t => t with { Metadata = meta }).ToList();

    public static string Sanitize(string? name)
    {
        var s = string.IsNullOrWhiteSpace(name) ? "export" : name.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Length == 0 ? "export" : s;
    }
}