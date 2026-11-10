using System.Text.Json;
using System.Text.Json.Serialization;

namespace TyuStock.Core.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON with atomic writes and corrupt-file recovery.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();
    public string FilePath { get; }
    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Saved;

    public SettingsStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        FilePath = Path.Combine(dataDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var text = File.ReadAllText(FilePath);
                    Current = (JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings()).Normalize();
                    return Current;
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                // Keep the broken file for inspection and start from defaults.
                try { File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
            }
            Current = new AppSettings().Normalize();
            return Current;
        }
    }

    public void Save(AppSettings? settings = null)
    {
        lock (_gate)
        {
            Current = (settings ?? Current).Normalize();
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        Saved?.Invoke(this, Current);
    }

    /// <summary>Mutate and persist in one step.</summary>
    public void Update(Action<AppSettings> change)
    {
        lock (_gate) change(Current);
        Save();
    }
}
