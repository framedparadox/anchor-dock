using System.Text.Json;
using System.Text.Json.Serialization;
using Anchor.Models;

namespace Anchor.Services;

/// <summary>Loads and saves the dock configuration as JSON under %AppData%\Anchor.</summary>
public static class DockStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anchor");

    private static readonly string FilePath = Path.Combine(Dir, "dock.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool Exists => File.Exists(FilePath);

    public static DockConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<DockConfig>(json, Options);
                if (cfg is not null)
                    return cfg;
            }
        }
        catch (Exception ex)
        {
            Diag.Log("DockStore.Load failed: " + ex.Message);
        }
        return new DockConfig();
    }

    public static void Save(DockConfig config)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(config, Options);
            // Write-then-rename for crash safety.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diag.Log("DockStore.Save failed: " + ex.Message);
        }
    }
}
