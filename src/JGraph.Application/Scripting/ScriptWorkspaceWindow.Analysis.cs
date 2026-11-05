using System.Windows.Threading;
using JGraph.Scripting.Analysis;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Code Analyzer over the shell's editors (App Designer window plan W10): a MATLAB document is analyzed when it
/// opens and half a second after its last keystroke, as R2025b's editor analyzes as you type, and its editor shows
/// the messages. A JGS document is not analyzed: the checks are MATLAB's.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private static readonly TimeSpan AnalysisPause = TimeSpan.FromMilliseconds(500);

    private void WireAnalysis(DocumentEntry entry)
    {
        if (entry.Model.Language != "MATLAB")
        {
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = AnalysisPause };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            AnalyzeDocument(entry);
        };
        entry.Editor.TextChanged += (_, _) =>
        {
            timer.Stop();
            timer.Start();
        };
        entry.Editor.FixApplied += (_, _) =>
        {
            timer.Stop();
            AnalyzeDocument(entry);
        };
        entry.Document.Closed += (_, _) => timer.Stop();
        AnalyzeDocument(entry);
    }

    private static void AnalyzeDocument(DocumentEntry entry)
    {
        string name = entry.Model.FilePath ?? entry.Model.FileName;
        entry.Editor.SetCodeMessages(AnalyzeText(entry.Editor.ScriptText, name));
    }

    /// <summary>The analyzer's messages over <paramref name="text"/>; a fault in the analyzer leaves the editor unmarked rather than failing the window.</summary>
    internal static IReadOnlyList<CodeMessage> AnalyzeText(string text, string name)
    {
        try
        {
            return JgsCodeAnalyzer.Analyze(text, name).Messages;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }
}
