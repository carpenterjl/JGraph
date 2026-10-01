using JGraph.Scripting;

namespace JGraph.Application.Scripting;

/// <summary>
/// The window's half of the Devices and Serial Explorer panes (device classes plan, stage 14,
/// ADR 0197): what their requests do here, and the door <c>serialExplorer</c> comes through.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>What <c>serialExplorer</c> reaches from a script: shows the pane, on the UI thread.</summary>
    private IScriptDeviceWindows? _deviceWindows;

    private void InitializeDevicePanes()
    {
        _deviceWindows = new DeviceWindows(this);
        DevicesPanel.CodeRequested += WriteAtPrompt;
        DevicesPanel.StatusChanged += SetStatus;
        DevicesPanel.SerialExplorerRequested += port =>
        {
            ShowPane("serialexplorer");
            SerialExplorerPanel.SelectPort(port);
        };
        SerialExplorerPanel.CodeRequested += WriteAtPrompt;
        SerialExplorerPanel.StatusChanged += SetStatus;
    }

    /// <summary>
    /// Writes a line of code at the console prompt and puts the caret after it. The line is not run:
    /// the user reads it, edits the baud rate or the variable's name, and presses Enter. A pane never
    /// opens a device by being clicked.
    /// </summary>
    private void WriteAtPrompt(string code)
    {
        ShowPane("console");
        ConsolePrompt.Text = code;
        ConsolePrompt.CaretIndex = code.Length;
        ConsolePrompt.Focus();
        SetStatus("Press Enter to run the line at the prompt.");
    }

    /// <summary>The app's <see cref="IScriptDeviceWindows"/>. Called on the engine's thread, so it posts to the window's.</summary>
    private sealed class DeviceWindows(ScriptWorkspaceWindow window) : IScriptDeviceWindows
    {
        public void ShowSerialExplorer() => window.Dispatcher.BeginInvoke(() => window.ShowPane("serialexplorer"));
    }
}
