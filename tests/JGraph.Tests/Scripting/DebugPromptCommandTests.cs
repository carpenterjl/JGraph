using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>The debugger words the paused prompt recognises, and the statements it must leave alone.</summary>
public class DebugPromptCommandTests
{
    [Theory]
    [InlineData("dbcont", DebugPromptVerb.Continue, 1)]
    [InlineData("  dbcont ;", DebugPromptVerb.Continue, 1)]
    [InlineData("dbstep", DebugPromptVerb.Step, 1)]
    [InlineData("dbstep 3", DebugPromptVerb.Step, 3)]
    [InlineData("dbstep in", DebugPromptVerb.StepIn, 1)]
    [InlineData("dbstep out", DebugPromptVerb.StepOut, 1)]
    [InlineData("dbquit", DebugPromptVerb.Quit, 1)]
    [InlineData("dbstack,", DebugPromptVerb.Stack, 1)]
    [InlineData("dbup", DebugPromptVerb.Up, 1)]
    [InlineData("dbup 2", DebugPromptVerb.Up, 2)]
    [InlineData("dbdown", DebugPromptVerb.Down, 1)]
    public void RecognisesTheDebuggerWords(string input, DebugPromptVerb verb, int count)
    {
        Assert.True(DebugPromptCommand.TryParse(input, out DebugPromptCommand? command));
        Assert.Equal(verb, command!.Verb);
        Assert.Equal(count, command.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("x = dbcont")]
    [InlineData("dbcont 2")]
    [InlineData("dbstep sideways")]
    [InlineData("dbstep 0")]
    [InlineData("dbstep -1")]
    [InlineData("DBCONT")]
    [InlineData("dbstop in main at 3")]
    [InlineData("disp(1)")]
    public void LeavesEverythingElseToTheInterpreter(string input)
    {
        Assert.False(DebugPromptCommand.TryParse(input, out DebugPromptCommand? command));
        Assert.Null(command);
    }
}
