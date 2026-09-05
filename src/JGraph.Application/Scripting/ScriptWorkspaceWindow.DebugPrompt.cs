using System.IO;
using System.Windows.Controls;
using JGraph.Scripting;
using JGraph.Scripting.Jgs.Debug;
using JGraph.Scripting.Workspace;

namespace JGraph.Application.Scripting;

/// <summary>
/// The paused prompt — MATLAB's <c>K&gt;&gt;</c>. While a debugged script is stopped at a statement,
/// the console prompt talks to the paused frame instead of the idle session: a typed statement reads
/// and writes the frame's variables, the debugger words (<c>dbcont</c>, <c>dbstep</c>, <c>dbquit</c>,
/// <c>dbstack</c>, <c>dbup</c>, <c>dbdown</c>) drive the run, and the Call Stack pane chooses which
/// frame the prompt and the Workspace pane are looking at.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    /// <summary>The call-stack frame the prompt evaluates in and the Workspace pane shows (0 = innermost).</summary>
    private int _selectedFrame;

    /// <summary>The interrupt for the typed statement in flight, or null when none is running.</summary>
    private System.Threading.CancellationTokenSource? _evaluationCts;

    /// <summary>How many more <c>dbstep N</c> steps to take automatically at the next pauses.</summary>
    private int _stepsRemaining;

    /// <summary>Whether a <c>K&gt;&gt;</c> statement is borrowing the paused interpreter — no stepping until it is done.</summary>
    private bool IsEvaluating => _evaluationCts is not null;

    private const string IdlePrompt = ">>";
    private const string PausedPrompt = "K>>";

    /// <summary>
    /// Runs one line typed while paused: a debugger word acts on the run, anything else is evaluated
    /// in the selected frame. The echo carries the <c>K&gt;&gt;</c> prompt so the transcript shows
    /// which statements ran inside the paused script.
    /// </summary>
    private async Task RunDebugPromptAsync(string code)
    {
        foreach (string line in code.Split('\n'))
        {
            AppendConsole(PausedPrompt + " " + line.TrimEnd('\r'));
        }

        if (DebugPromptCommand.TryParse(code, out DebugPromptCommand? command))
        {
            ExecuteDebugCommand(command!);
            ConsolePrompt.Focus();
            return;
        }

        if (EditPromptCommand.TryParse(code, out EditPromptCommand? edit))
        {
            RunEditCommand(edit!);
            ConsolePrompt.Focus();
            return;
        }

        await EvaluateAtPausedPromptAsync(code).ConfigureAwait(true);
        ConsolePrompt.Focus();
    }

    /// <summary>
    /// Evaluates one statement in the selected paused frame and reports it — shared by typed
    /// <c>K&gt;&gt;</c> input and the writes the Data Viewer composes while paused.
    /// </summary>
    private async Task EvaluateAtPausedPromptAsync(string code)
    {
        if (_debugSession is not { IsPaused: true } session)
        {
            AppendConsole("--- The script is no longer paused. ---");
            return;
        }

        _evaluationCts = new System.Threading.CancellationTokenSource();
        UpdateCommandStates();
        ScriptRunResult result;
        try
        {
            result = await session.EvaluateAsync(code, _selectedFrame, _evaluationCts.Token).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            // The run ended (or the frame went away) between the keystroke and the evaluation.
            result = ScriptRunResult.Failed(ex.Message);
        }
        finally
        {
            _evaluationCts.Dispose();
            _evaluationCts = null;
            UpdateCommandStates();
        }

        if (!result.Success)
        {
            AppendConsole($"--- Failed: {result.Message} ---");
            SetStatus($"Failed: {result.Message}");
        }
        else if (_debugSession is { IsPaused: true })
        {
            ShowVariables(result.Variables, _session.RunningLanguage);
            SetStatus($"{PausedPrompt} done — {result.Variables.Count} variable(s) in {FrameName(_selectedFrame)}.");
        }
    }

    private void ExecuteDebugCommand(DebugPromptCommand command)
    {
        if (_debugSession is not { IsPaused: true } session)
        {
            AppendConsole("--- The script is not paused. ---");
            return;
        }

        switch (command.Verb)
        {
            case DebugPromptVerb.Continue:
                RunOrContinue();
                break;

            case DebugPromptVerb.Step:
                // dbstep N: the first step now, the rest re-armed at each Step pause. A breakpoint
                // reached on the way cancels the remainder, as MATLAB's does.
                _stepsRemaining = command.Count - 1;
                StepCommand(static s => s.StepOver());
                break;

            case DebugPromptVerb.StepIn:
                StepCommand(static s => s.StepIn());
                break;

            case DebugPromptVerb.StepOut:
                StepCommand(static s => s.StepOut());
                break;

            case DebugPromptVerb.Quit:
                StopRun();
                break;

            case DebugPromptVerb.Stack:
                PrintCallStack(session);
                break;

            case DebugPromptVerb.Up:
                MoveFrame(+command.Count);
                break;

            case DebugPromptVerb.Down:
                MoveFrame(-command.Count);
                break;
        }
    }

    /// <summary>MATLAB's <c>dbstack</c> listing: innermost first, the prompt's frame marked.</summary>
    private void PrintCallStack(JgsDebugSession session)
    {
        IReadOnlyList<JgsStackFrame> frames;
        try
        {
            frames = session.GetCallStack();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        for (int i = 0; i < frames.Count; i++)
        {
            JgsStackFrame frame = frames[i];
            string marker = i == _selectedFrame ? ">" : " ";
            string file = frame.SourceId.Length == 0 ? "" : "  " + Path.GetFileName(frame.SourceId);
            AppendConsole($"{marker} In {frame.FunctionName} (line {frame.Line}){file}");
        }
    }

    /// <summary><c>dbup</c>/<c>dbdown</c>: move the prompt's frame towards the caller (+) or the pause (−).</summary>
    private void MoveFrame(int delta)
    {
        int count = CallStackList.Items.Count;
        if (count == 0)
        {
            return;
        }

        int target = _selectedFrame + delta;
        if (target < 0)
        {
            AppendConsole("Already in the innermost workspace.");
            return;
        }

        if (target >= count)
        {
            AppendConsole("Already in the base workspace.");
            return;
        }

        SelectFrame(target);
        if (CallStackList.Items[target] is JgsStackFrame frame)
        {
            AppendConsole($"In workspace belonging to {frame.FunctionName} (line {frame.Line})");
        }
    }

    /// <summary>Selects a frame in the Call Stack pane; the selection handler does the showing.</summary>
    private void SelectFrame(int index)
    {
        if (CallStackList.SelectedIndex == index)
        {
            ShowFrame(index);
        }
        else
        {
            CallStackList.SelectedIndex = index;
        }
    }

    private void OnCallStackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CallStackList.SelectedIndex >= 0)
        {
            ShowFrame(CallStackList.SelectedIndex);
        }
    }

    /// <summary>
    /// Points the Workspace pane, the prompt and the execution marker at one frame of the paused
    /// call stack — the pane's click and <c>dbup</c>/<c>dbdown</c> both end here.
    /// </summary>
    private void ShowFrame(int index)
    {
        if (_debugSession is not { IsPaused: true } session
            || index < 0 || index >= CallStackList.Items.Count
            || CallStackList.Items[index] is not JgsStackFrame frame)
        {
            return;
        }

        _selectedFrame = index;
        try
        {
            ShowVariables(session.GetVariables(index), _session.RunningLanguage);
        }
        catch (InvalidOperationException)
        {
            return; // the run finished underneath the click
        }

        ClearExecutionMarkers();
        DocumentEntry? entry = FindOrOpenDocument(frame.SourceId);
        if (entry is not null)
        {
            entry.Document.IsActive = true;
            entry.Editor.SetCurrentLine(frame.Line);
        }
    }

    private string FrameName(int index) =>
        index >= 0 && index < CallStackList.Items.Count && CallStackList.Items[index] is JgsStackFrame frame
            ? frame.FunctionName
            : "the paused frame";
}
