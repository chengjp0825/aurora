using System.Windows;
using MyQuicker.Interop;
using MyQuicker.Models;
using MyQuicker.Services;

namespace MyQuicker.UI;

/// <summary>
/// Interaction logic for SettingsWindow.xaml. Two-tab settings center:
/// 常规 (wake-up key) and 动作管理 (editable action list). Per SPEC step 7.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly GlobalHookService _hookService;
    private readonly SettingsManager _settingsManager;
    private readonly AppSettings _settings;

    internal SettingsWindow(GlobalHookService hookService, SettingsManager settingsManager)
    {
        InitializeComponent();
        _hookService = hookService;
        _settingsManager = settingsManager;

        _settings = _settingsManager.Load();
        WakeupKeyCombo.SelectedIndex = ToIndex(_settings);
        ActionsGrid.ItemsSource = _settings.Actions;
    }

    private static int ToIndex(AppSettings s)
    {
        if (s.WakeupMessage == NativeMethods.WM_XBUTTONDOWN)
            return s.XButtonData == 2 ? 2 : 1;
        return 0; // middle
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Commit any in-flight cell edit before persisting.
        ActionsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);

        int index = WakeupKeyCombo.SelectedIndex;
        _settings.WakeupMessage = index == 1 || index == 2
            ? NativeMethods.WM_XBUTTONDOWN
            : NativeMethods.WM_MBUTTONDOWN;
        _settings.XButtonData = index switch { 1 => 1, 2 => 2, _ => 0 };

        _settingsManager.Save(_settings);
        _hookService.UpdateSettings(_settings);
        Close();
    }
}
