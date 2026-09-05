using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

/// <summary>The <c>edit</c> and <c>open</c> words the prompt recognises, in both of MATLAB's syntaxes.</summary>
public class EditPromptCommandTests
{
    [Theory]
    [InlineData("edit", EditPromptVerb.Edit, null)]
    [InlineData("edit;", EditPromptVerb.Edit, null)]
    [InlineData("edit foo", EditPromptVerb.Edit, "foo")]
    [InlineData("edit foo.m;", EditPromptVerb.Edit, "foo.m")]
    [InlineData("edit sub\\foo.m", EditPromptVerb.Edit, "sub\\foo.m")]
    [InlineData("edit('foo')", EditPromptVerb.Edit, "foo")]
    [InlineData("edit(\"foo bar.m\")", EditPromptVerb.Edit, "foo bar.m")]
    [InlineData("edit 'foo bar.m'", EditPromptVerb.Edit, "foo bar.m")]
    [InlineData("open data.csv", EditPromptVerb.Open, "data.csv")]
    [InlineData("open('x')", EditPromptVerb.Open, "x")]
    public void RecognisesEditAndOpen(string input, EditPromptVerb verb, string? argument)
    {
        Assert.True(EditPromptCommand.TryParse(input, out EditPromptCommand? command));
        Assert.Equal(verb, command!.Verb);
        Assert.Equal(argument, command.Argument);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("open()")]
    [InlineData("edited = 1")]
    [InlineData("editor('x')")]
    [InlineData("x = edit")]
    [InlineData("open(v)")]
    [InlineData("")]
    public void LeavesEverythingElseToTheInterpreter(string input)
    {
        Assert.False(EditPromptCommand.TryParse(input, out EditPromptCommand? command));
        Assert.Null(command);
    }
}
