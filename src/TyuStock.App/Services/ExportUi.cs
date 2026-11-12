using TyuStock.Core.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using TyuStock.Core.Export;

namespace TyuStock.App.Services;

/// <summary>Shared Save-file dialog for CSV (UTF-8 BOM) and XLSX. Returns a localized status string; never throws to the UI.</summary>
public static class ExportUi
{
    public static string? Save(string suggestedName, IReadOnlyList<ExportTable> tables, LocalizationService loc)
    {
        if (tables.Count == 0 || tables.All(t => t.Rows.Count == 0))
            return loc["Ex_NoData"];
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = loc["Ex_DialogTitle"],
                FileName = Sanitize(suggestedName),
                Filter = loc["Ex_Filter"],
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dlg.ShowDialog(Application.Current?.MainWindow) != true)
                return null;

            var path = dlg.FileName;
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
                    {
                        var safe = Sanitize(table.Name);
                        TableExporter.WriteCsv(table, Path.Combine(dir, $"{stem}_{safe}.csv"));
                    }
                }
            }
            else
            {
                if (!path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    path += ".xlsx";
                TableExporter.WriteXlsx(tables, path);
            }
            return string.Format(loc["Ex_Saved"], path);
        }
        catch (Exception ex)
        {
            AppLog.Error("export", "export failed", ex);
            return string.Format(loc["Ex_Failed"], ex.Message);
        }
    }

    public static string Sanitize(string? name)
    {
        var s = string.IsNullOrWhiteSpace(name) ? "export" : name.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s.Length == 0 ? "export" : s;
    }
}