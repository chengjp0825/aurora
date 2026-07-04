using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyQuicker.Models;

/// <summary>
/// A single user-configurable action. Implements INotifyPropertyChanged so
/// the DataGrid in the settings window can two-way bind to its cells.
/// Per SPEC.md §4.3 / step 7.
/// </summary>
public class ActionItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _command = string.Empty;
    private string _arguments = string.Empty;
    private string _icon = "EFA8"; // Segoe MDL2 Assets 图标码（hex），空/无效回退占位字

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Command
    {
        get => _command;
        set => SetField(ref _command, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetField(ref _arguments, value);
    }

    /// <summary>动作图标码（Segoe MDL2 Assets 的 hex 码，如 "EFA8"）。显示时转字形，无效回退占位字。</summary>
    public string Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
