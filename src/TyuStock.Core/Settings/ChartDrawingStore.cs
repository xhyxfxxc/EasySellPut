using System.Text.Json;
using System.Text.Json.Serialization;

namespace TyuStock.Core.Settings;

public enum DrawingKind { HorizontalLine, TrendLine }

/// <summary>B5: a line the user drew on a symbol's K-line chart. A horizontal line only uses <see cref="Price1"/>.</summary>
public sealed record ChartDrawing(DrawingKind Kind, double Price1, DateTimeOffset? Time1 = null, double Price2 = 0, DateTimeOffset? Time2 = null);

/// <summary>B5: drawings per symbol in <c>drawings.json</c> (used from the UI thread only).</summary>
public sealed class ChartDrawingStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private Dictionary<string, List<ChartDrawing>> _bySymbol = new(StringComparer.OrdinalIgnoreCase);
    public string FilePath { get; }

    public ChartDrawingStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        FilePath = Path.Combine(dataDirectory, "drawings.json");
        if (!File.Exists(FilePath)) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<ChartDrawing>>>(File.ReadAllText(FilePath), Json);
            if (loaded is not null) _bySymbol = new(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            Diagnostics.AppLog.Error("drawings", $"{FilePath} is unreadable, starting without drawings", ex);
        }
    }

    public IReadOnlyList<ChartDrawing> For(string symbol) => _bySymbol.TryGetValue(symbol, out var list) ? list : [];

    public void Add(string symbol, ChartDrawing drawing)
    {
        if (!_bySymbol.TryGetValue(symbol, out var list)) _bySymbol[symbol] = list = [];
        list.Add(drawing);
        Save();
    }

    public void Clear(string symbol)
    {
        if (_bySymbol.Remove(symbol)) Save();
    }

    private void Save()
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_bySymbol, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
