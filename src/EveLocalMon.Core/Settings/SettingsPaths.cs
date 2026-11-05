namespace EveLocalMon.Core.Settings;

public static class SettingsPaths
{
    /// <summary>Set to a folder to keep settings and templates there instead (a portable install, or a
    /// test run that must not touch the real ones).</summary>
    public const string DataDirectoryVariable = "EVELOCALMON_DATA_DIR";

    /// <summary>
    /// %APPDATA%\EveLocalMon on Windows, ~/Library/Application Support/EveLocalMon on macOS.
    /// (.NET maps SpecialFolder.ApplicationData to ~/.config on macOS, which is not where a
    /// Mac app's data belongs, so that case is spelled out.)
    /// </summary>
    public static string RootDirectory { get; } = BuildRootDirectory();

    public static string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public static string TemplatesDirectory => Path.Combine(RootDirectory, "templates");

    private static string BuildRootDirectory()
    {
        if (Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden)
        {
            return overridden;
        }

        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "EveLocalMon");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EveLocalMon");
    }
}
