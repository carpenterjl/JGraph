using System.Reflection;
using System.Text.Json;

namespace JGraph.Scripting.Designer;

/// <summary>
/// One of App Designer's actions as R2025b's <c>ActionManager</c> registers it (stage W0's
/// <c>w0_actions.json</c>): its id, its words, and the stage of the App Designer window plan that
/// makes JGraph run it.
/// </summary>
/// <param name="Id">The action id (<c>AD_SAVE</c>), as the toolstrip's controls name it.</param>
/// <param name="Name">The id's name in <c>ActionIds</c> (<c>SAVE</c>).</param>
/// <param name="Text">The label, or null.</param>
/// <param name="Description">The tooltip, or null.</param>
/// <param name="Shortcut">The shortcut in R2025b's words (<c>Ctrl+S</c>), or null.</param>
/// <param name="EditorActionId">The rich-text editor's action it stands for (<c>rtc_step</c>), or null.</param>
/// <param name="Stage">Null when JGraph runs it; else the stage that will (<c>W4</c>) or the out-of-scope row that keeps it out (<c>X1</c>).</param>
public sealed record DesignAction(string Id, string Name, string? Text, string? Description, string? Shortcut, string? EditorActionId, string? Stage)
{
    /// <summary>Whether JGraph runs the action.</summary>
    public bool IsLive => Stage is null;
}

/// <summary>A row of App Designer's Keyboard Shortcuts dialog.</summary>
/// <param name="Action">What the keys do, in R2025b's words.</param>
/// <param name="Keys">The keys, as the dialog shows them.</param>
public sealed record DesignShortcut(string Action, string Keys);

/// <summary>
/// App Designer's action registry (App Designer window plan, stage W2): all 202 actions R2025b's
/// client registers, each with the stage that makes JGraph run it, so the toolstrip, the context
/// menus and the keys reach one list. An action whose stage has not landed is shown, disabled, with
/// R2025b's words.
/// </summary>
public static class DesignActions
{
    private static readonly Lazy<IReadOnlyDictionary<string, DesignAction>> Registered = new(Read);

    /// <summary>
    /// The actions JGraph runs since stage W2: the file commands it had, the grid and grouping
    /// commands, the Code View's insertions and Code Options, Find (the code editor's own search),
    /// cut, copy and paste in the Code View, help, the shortcuts dialog and the document tab's menu.
    /// The grid layout actions run from the canvas's context menu (designer plan DS4).
    /// </summary>
    private static readonly HashSet<string> Live = new(StringComparer.Ordinal)
    {
        "AD_SAVE", "AD_SAVEAS", "AD_OPEN", "AD_RUN", "AD_UNDO", "AD_REDO", "AD_DELETE", "AD_HELP",
        "AD_CUT", "AD_COPY", "AD_PASTE",
        "AD_DESIGN_GRID_SHOW", "AD_DESIGN_GRID_SNAP", "AD_DESIGN_GRID_INTERVAL", "AD_DESIGN_GRID_INCREMENT", "AD_DESIGN_GRID_DECREMENT",
        "AD_DESIGN_VIEW_ALIGN_HINTS",
        "AD_DESIGN_GROUP_ALL_OPTIONS", "AD_DESIGN_GROUP_GROUP", "AD_DESIGN_GROUP_ADD", "AD_DESIGN_GROUP_REMOVE", "AD_DESIGN_GROUP_UNGROUP",
        "ADD_ROW", "ADD_COLUMN", "DELETE_ROW", "DELETE_COLUMN", "GRIDIFY", "UNGRIDIFY",
        "AD_EDITOR_ADD_CALLBACK", "AD_EDITOR_ADD_FUNCTION", "AD_EDITOR_ADD_PRIVATE_FUNCTION", "AD_EDITOR_ADD_PUBLIC_FUNCTION",
        "AD_EDITOR_ADD_PROPERTY", "AD_EDITOR_ADD_PRIVATE_PROPERTY", "AD_EDITOR_ADD_PUBLIC_PROPERTY",
        "AD_INPUT_ARGUMENTS", "AD_TOGGLE_SINGLETON", "AD_INSERT_AT_CURSOR", "AD_EDITOR_FIND",
        "AD_OPEN_SHORTCUTS_DIALOG",
        "CHANGE_CURRENT_WORKING_DIRECTORY", "ADD_TO_SEARCH_PATH", "REMOVE_FROM_SEARCH_PATH", "OPEN_IN_FILE_MANAGER", "COPY_TO_CLIPBOARD",
    };

    /// <summary>Every action, by id.</summary>
    public static IReadOnlyDictionary<string, DesignAction> All => Registered.Value;

    /// <summary>The action with this id, or null.</summary>
    public static DesignAction? Find(string? id) => id is not null && All.TryGetValue(id, out DesignAction? action) ? action : null;

    /// <summary>Whether JGraph runs the action with this id (an id R2025b does not register is not run).</summary>
    public static bool IsLive(string? id) => Find(id)?.IsLive == true;

    /// <summary>
    /// R2025b's Keyboard Shortcuts dialog (Ctrl+/), the table <c>ShortcutsDialogContent</c> builds:
    /// eleven fixed rows, not the action list. "Perform a universal search" is left out, as JGraph
    /// has no universal search (a divergence).
    /// </summary>
    public static IReadOnlyList<DesignShortcut> Shortcuts { get; } =
    [
        new("Run the active file", "F5"),
        new("Close the active file", "Ctrl + W"),
        new("Quit App Designer", "Ctrl + Q"),
        new("Alternate between design and code view", "Shift + F7"),
        new("In the design area, move the selection to the next component in the navigation sequence", "Tab"),
        new("In the design area, move the selection to the previous component in the navigation sequence", "Shift + Tab"),
        new("Zoom in/out on the canvas", "Ctrl+Plus/Minus"),
        new("Reset the canvas zoom to default", "Ctrl + Alt + 0"),
        new("Zoom to fit the canvas to the view", "Space"),
        new("Show this list of shortcuts", "Ctrl + /"),
    ];

    /// <summary>The stage of the App Designer window plan that makes JGraph run an action JGraph does not run yet.</summary>
    internal static string StageOf(string id, string name)
    {
        if (Live.Contains(id))
        {
            return "";
        }

        return name switch
        {
            _ when name.Contains("SIMULINK", StringComparison.Ordinal) => "X1",
            "OPENGUIDEAPP" => "X3",
            "SHARE_WEB" => "X2",
            "EDITOR_SPELL_CHECKER" => "X9",
            "NEW" or "NEW2COLAPP" or "NEW3COLAPP" or "NEWCOMPONENTAPP" or "NEWCOMPONENTGRIDAPP" or "OPEN_STARTSCREEN_DIALOG" => "W3",
            _ when name.StartsWith("EXAMPLE_", StringComparison.Ordinal) || name.StartsWith("TUTORIAL_", StringComparison.Ordinal) => "W3",
            _ when name.StartsWith("DESIGN_ALIGN_", StringComparison.Ordinal) || name.StartsWith("DESIGN_SIZE_", StringComparison.Ordinal)
                || name.StartsWith("DESIGN_SPACING_", StringComparison.Ordinal) || name.StartsWith("DESIGN_REORDER_COMPONENT_", StringComparison.Ordinal)
                || name.StartsWith("DESIGN_TABORDER_", StringComparison.Ordinal) || name.StartsWith("DESIGN_FONT_", StringComparison.Ordinal)
                || name == "ADD_LABEL" => "W4",
            "DUPLICATE" or "SELECT_ALL" or "EDIT_MODE" => "W5",
            _ when name.StartsWith("DESIGN_MENU", StringComparison.Ordinal) || name.StartsWith("DESIGN_TREE_", StringComparison.Ordinal)
                || name.StartsWith("DESIGN_TOOL", StringComparison.Ordinal) || name.StartsWith("DESIGN_REORDER_", StringComparison.Ordinal) => "W5",
            "ZOOM_IN" or "ZOOM_OUT" or "RESET_ZOOM" or "FIT_TO_VIEW" or "DESIGN_VIEW_DISPLAY_SIZE" => "W6",
            "APP_DETAILS" or "HELP_COMMENTS" or "SAVECOPY_AS" or "EXPORT" => "W8",
            _ when name.StartsWith("RESPONSIFY", StringComparison.Ordinal) || name.StartsWith("UNRESPONSIFY", StringComparison.Ordinal) => "W8",
            "EDITOR_PAUSE" or "EDITOR_CONTINUE" or "EDITOR_STEP" or "EDITOR_STEP_IN" or "EDITOR_STEP_OUT" or "EDITOR_STOP" => "W11",
            _ when name.Contains("BREAKPOINT", StringComparison.Ordinal) || name.StartsWith("EDITOR_TOGGLE_STOP_IF", StringComparison.Ordinal)
                || name == "EDITOR_TOGGLE_CONDITIONAL" => "W11",
            _ when name.StartsWith("EDITOR_", StringComparison.Ordinal) && !name.StartsWith("EDITOR_ADD_", StringComparison.Ordinal) => "W9",
            "SHARE_ALL_OPTIONS" or "SHARE_DESKTOP" => "W12",
            "SHARE_MATLAB" => "W13",
            "OPEN_FILE_COMPARISON_DIALOG" => "W14",
            _ => "W15", // Configure for Apps, events, the managed properties of a custom component
        };
    }

    private static IReadOnlyDictionary<string, DesignAction> Read()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JGraph.Scripting.designer-actions.json")
            ?? throw new InvalidOperationException("App Designer's action recording is missing from the assembly.");
        using JsonDocument recording = JsonDocument.Parse(stream);
        var actions = new Dictionary<string, DesignAction>(StringComparer.Ordinal);
        foreach (JsonElement a in recording.RootElement.GetProperty("actions").EnumerateArray())
        {
            string id = a.GetProperty("id").GetString()!;
            string name = a.GetProperty("name").GetString()!;
            string stage = StageOf(id, name);
            actions[id] = new DesignAction(id, name, Text(a, "text"), Text(a, "description"), Text(a, "shortcut"), Text(a, "appDesignerEditorActionId"),
                stage.Length == 0 ? null : stage);
        }

        return actions;
    }

    private static string? Text(JsonElement a, string name) =>
        a.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
