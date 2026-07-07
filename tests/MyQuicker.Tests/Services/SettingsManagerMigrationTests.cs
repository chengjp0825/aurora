using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MyQuicker.Domain.DTO;
using MyQuicker.Services;
using Xunit;

#pragma warning disable CS0618 // 迁移测试天然需要操作旧版 Command 字段。

namespace MyQuicker.Tests.Services;

[Collection("SettingsManagerSerial")]
public class SettingsManagerMigrationTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _legacyPath;
    private readonly string _bakPath;

    public SettingsManagerMigrationTests()
    {
        _settingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        _legacyPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _bakPath = _settingsPath + ".bak";
        DeleteTestFiles();
    }

    public void Dispose()
    {
        DeleteTestFiles();
    }

    [Fact]
    public void MigrateActionCommandsIntoCatalog_MapsLegacyCommandStringToCatalog()
    {
        var settings = new Settings
        {
            MenuGroups = new List<MenuGroup>
            {
                new()
                {
                    Id = "default",
                    Actions = new List<ActionItem>
                    {
                        new() { Name = "Browser", Command = "https://example.com" },
                        new() { Name = "Notepad", Command = "C:\\Windows\\notepad.exe" },
                        new() { Name = "Screenshot", Command = "sys:snipping" },
                    },
                },
            },
        };

        SettingsManager.MigrateActionCommandsIntoCatalog(settings);

        Assert.Equal("sys:snipping", settings.MenuGroups[0].Actions[2].CommandId);

        var browserCommand = settings.Commands[0];
        Assert.Equal(CommandType.OpenUrl, browserCommand.Type);
        Assert.Equal("https://example.com", browserCommand.Target);

        var notepadCommand = settings.Commands[1];
        Assert.Equal(CommandType.LaunchApplication, notepadCommand.Type);
        Assert.Equal("C:\\Windows\\notepad.exe", notepadCommand.Target);

        Assert.Equal(browserCommand.Id, settings.MenuGroups[0].Actions[0].CommandId);
        Assert.Equal(notepadCommand.Id, settings.MenuGroups[0].Actions[1].CommandId);
    }

    [Fact]
    public void Load_MigratesOldSettingsFormatWithActionProperty()
    {
        string oldJson = @"{
            ""Action"": {
                ""WakeupMessage"": -1,
                ""InterceptWakeupKey"": false,
                ""CircleSensitivity"": 2,
                ""Actions"": [
                    { ""Name"": ""Browser"", ""Command"": ""https://example.com"", ""Icon"": ""E71E"" }
                ]
            },
            ""Snipping"": { ""DragThreshold"": 5 },
            ""Menu"": { ""Width"": 300 }
        }";
        File.WriteAllText(_settingsPath, oldJson);

        var manager = new SettingsManager();
        Settings settings = manager.Load();

        Assert.Single(settings.TriggerBindings);
        Assert.Equal(TriggerType.CircleGesture, settings.TriggerBindings[0].Type);
        Assert.Equal(CircleSensitivity.High, settings.TriggerBindings[0].CircleSensitivity);
        Assert.False(settings.TriggerBindings[0].InterceptWakeupKey);

        Assert.Single(settings.MenuGroups);
        Assert.Single(settings.MenuGroups[0].Actions);
        Assert.Equal("Browser", settings.MenuGroups[0].Actions[0].Name);
        Assert.NotEmpty(settings.Commands);
        Assert.Equal(CommandType.OpenUrl, settings.Commands[0].Type);
    }

    [Fact]
    public void Load_MigratesLegacyAppsettingsJson()
    {
        string legacyJson = @"{
            ""WakeupMessage"": 516,
            ""XButtonData"": 1,
            ""Actions"": [
                { ""Name"": ""Notepad"", ""Command"": ""C:\\Windows\\notepad.exe"" }
            ]
        }";
        File.WriteAllText(_legacyPath, legacyJson);

        var manager = new SettingsManager();
        Settings settings = manager.Load();

        Assert.Single(settings.TriggerBindings);
        Assert.Equal(TriggerType.Button, settings.TriggerBindings[0].Type);
        Assert.Equal(1, settings.TriggerBindings[0].XButtonData);

        Assert.Single(settings.MenuGroups);
        Assert.Single(settings.MenuGroups[0].Actions);
        Assert.Equal("Notepad", settings.MenuGroups[0].Actions[0].Name);

        // Migration should also have persisted a new settings.json.
        Assert.True(File.Exists(_settingsPath));
    }

    [Fact]
    public void Load_FirstRun_SeedsDefaultActionsWithIcons()
    {
        var manager = new SettingsManager();
        Settings settings = manager.Load();

        Assert.Single(settings.MenuGroups);
        var defaultGroup = settings.MenuGroups[0];
        Assert.Equal(4, defaultGroup.Actions.Count);

        Assert.Contains(defaultGroup.Actions, a =>
            a.Name == "计算器" && a.CommandId == "cmd:calc" && a.Icon == "E94C");
        Assert.Contains(defaultGroup.Actions, a =>
            a.Name == "记事本" && a.CommandId == "cmd:notepad" && a.Icon == "E8A5");
        Assert.Contains(defaultGroup.Actions, a =>
            a.Name == "我的网页" && a.CommandId == "cmd:moongazer" && a.Icon == "E71E");
        Assert.Contains(defaultGroup.Actions, a =>
            a.Name == "截图" && a.CommandId == "sys:snipping" && a.Icon == "E70F");

        Assert.Contains(settings.Commands, c => c.Id == "cmd:calc" && c.Target == "calc.exe");
        Assert.Contains(settings.Commands, c => c.Id == "cmd:notepad" && c.Target == "notepad.exe");
        Assert.Contains(settings.Commands, c => c.Id == "cmd:moongazer" && c.Target == "https://moongazer.cn");
    }

    [Fact]
    public void Load_CorruptSettingsFile_BackupsAndReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "this is not json {[");

        var manager = new SettingsManager();
        Settings settings = manager.Load();

        Assert.NotNull(settings);
        Assert.True(File.Exists(_bakPath), "损坏的配置文件应被重命名为 .bak");
    }

    private void DeleteTestFiles()
    {
        TryDelete(_settingsPath);
        TryDelete(_legacyPath);
        TryDelete(_bakPath);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 清理失败不影响测试断言。
        }
    }
}