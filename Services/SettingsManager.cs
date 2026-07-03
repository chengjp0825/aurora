using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MyQuicker.Models;

namespace MyQuicker.Services;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> (including the action list)
/// to appsettings.json, creating a default file with the sample actions
/// when missing. Per SPEC step 6/7.
/// </summary>
internal sealed class SettingsManager
{
    private const string SettingsFile = "appsettings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Loads settings from appsettings.json. If the file does not exist,
    /// a default one (with the sample actions) is created and returned.
    /// </summary>
    public AppSettings Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, SettingsFile);

        if (!File.Exists(path))
        {
            AppSettings defaults = CreateDefault();
            Save(defaults);
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(path);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
                return CreateDefault();

            settings.Actions ??= new List<ActionItem>();
            if (settings.Actions.Count == 0)
            {
                // Dirty/legacy file missing Actions: backfill the sample
                // actions and rewrite the file immediately.
                settings.Actions.AddRange(new[]
                {
                    new ActionItem { Name = "打开计算器", Command = "calc.exe" },
                    new ActionItem { Name = "打开记事本", Command = "notepad.exe" },
                    new ActionItem { Name = "访问我的网站", Command = "https://moongazer.cn" },
                });
                Save(settings); // 立刻回写修复脏文件
            }
            return settings;
        }
        catch
        {
            return CreateDefault();
        }
    }

    /// <summary>Saves the given settings to appsettings.json.</summary>
    public void Save(AppSettings settings)
    {
        string path = Path.Combine(AppContext.BaseDirectory, SettingsFile);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>Default settings containing the three sample actions.</summary>
    private static AppSettings CreateDefault() => new()
    {
        Actions = new List<ActionItem>
        {
            new() { Name = "打开计算器", Command = "calc.exe" },
            new() { Name = "打开记事本", Command = "notepad.exe" },
            new() { Name = "访问我的网站", Command = "https://moongazer.cn" },
        },
    };
}
