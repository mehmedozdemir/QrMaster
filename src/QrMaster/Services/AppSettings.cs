using System.Text.Json.Serialization;

namespace QrMaster.Services;

/// <summary>Update channels. A channel also receives everything more stable than itself.</summary>
public enum UpdateChannel
{
    /// <summary>Stable releases only (tag v1.2.3).</summary>
    Prod,
    /// <summary>Stable + test pre-releases (tag v1.2.3-test.N).</summary>
    Test,
    /// <summary>Everything, including nightly dev builds (tag v1.2.3-dev.N).</summary>
    Dev
}

public enum ThemeChoice { System, Light, Dark }

public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public string Language { get; set; } = "en";
    public UpdateChannel Channel { get; set; } = UpdateChannel.Prod;
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    private static string FilePath => Path.Combine(AppInfo.DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var fs = File.OpenRead(FilePath);
                var s = System.Text.Json.JsonSerializer.Deserialize(fs, SettingsJsonContext.Default.AppSettings);
                if (s is not null) return s;
            }
        }
        catch { /* corrupt settings: fall back to defaults */ }

        var defaults = new AppSettings();
        if (SemVer.TryParse(AppInfo.Version, out var v))
        {
            defaults.Channel = UpdateService.ChannelOf(v) ?? UpdateChannel.Prod;
        }
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDirectory);
            var tmp = FilePath + ".tmp";
            using (var fs = File.Create(tmp))
            {
                System.Text.Json.JsonSerializer.Serialize(fs, this, SettingsJsonContext.Default.AppSettings);
            }
            File.Move(tmp, FilePath, true);
        }
        catch { /* non-fatal */ }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
