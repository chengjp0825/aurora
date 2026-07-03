using System.Collections.Generic;
using MyQuicker.Interop;

namespace MyQuicker.Models;

/// <summary>
/// Application settings persisted to appsettings.json. Per SPEC step 6/7.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// The mouse message that wakes the menu (WM_MBUTTONDOWN or WM_XBUTTONDOWN).
    /// </summary>
    public int WakeupMessage { get; set; } = NativeMethods.WM_MBUTTONDOWN;

    /// <summary>
    /// For WM_XBUTTONDOWN: the side-button identifier extracted from the
    /// high word of mouseData (1 = back / XBUTTON1, 2 = forward / XBUTTON2).
    /// Ignored when WakeupMessage is WM_MBUTTONDOWN.
    /// </summary>
    public int XButtonData { get; set; } = 0;

    /// <summary>
    /// The action list, now managed alongside settings in appsettings.json.
    /// </summary>
    public List<ActionItem> Actions { get; set; } = new();
}
