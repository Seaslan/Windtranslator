using System;
using System.IO;
using System.Text.Json;
using Windtranslator.Models;

namespace Windtranslator.Services;

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Windtranslator",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOptions)
                    ?? new AppSettings();
                settings.TranslationHistory ??= new();
                settings.Endpoints ??= new(StringComparer.OrdinalIgnoreCase);
                settings.Models ??= new(StringComparer.OrdinalIgnoreCase);
                settings.AvailableModels ??= new(StringComparer.OrdinalIgnoreCase);
                settings.ImageEndpoints ??= new(StringComparer.OrdinalIgnoreCase);
                settings.ImageModels ??= new();
                return settings;
            }
        }
        catch
        {
            // Corrupt or inaccessible settings should not prevent the app from starting.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Settings persistence is best effort; API keys are never written here.
        }
    }
}
