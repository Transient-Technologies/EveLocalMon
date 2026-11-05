using System.Text.Json;

namespace EveLocalMon.Core.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON. Loading never throws: a missing, corrupt
/// or future-schema file degrades to defaults with a warning, because failing to start over a bad
/// settings file would be worse than losing the settings.
/// </summary>
public sealed class SettingsStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath { get; } = filePath ?? SettingsPaths.SettingsFile;

    public AppSettings Load() => Load(out _);

    public AppSettings Load(out string? warning)
    {
        warning = null;

        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (loaded is null)
            {
                warning = $"'{FilePath}' was empty; using defaults.";
                return new AppSettings();
            }

            if (loaded.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                warning = $"'{FilePath}' has schema version {loaded.SchemaVersion}, newer than this build " +
                    $"(v{AppSettings.CurrentSchemaVersion}); unknown fields were ignored.";
            }

            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = $"Could not read '{FilePath}' ({ex.Message}); using defaults.";
            return new AppSettings();
        }
    }

    /// <summary>Writes via a temp file + replace, so an interrupted save can't leave a half-written
    /// settings file behind.</summary>
    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Whatever version was loaded, what is written is the current one: an old file is upgraded the
        // first time it is saved, instead of carrying new blocks under an old version number.
        AppSettings current = settings with { SchemaVersion = AppSettings.CurrentSchemaVersion };

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(current, JsonOptions));
        File.Move(tempPath, FilePath, overwrite: true);
    }
}
