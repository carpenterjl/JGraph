using JGraph.Scripting.Workspace;
using Xunit;

namespace JGraph.Tests.Scripting;

public class ScriptSessionModelTests
{
    [Fact]
    public void CanRun_RequiresIdleState_AndAnAvailableLanguage()
    {
        var session = new ScriptSessionModel(new[] { "JGS", "C#" });

        Assert.True(session.CanRun("JGS"));
        Assert.True(session.CanRun("C#"));
        Assert.False(session.CanRun("Python"));   // engine unavailable
        Assert.False(session.CanRun(null));
        Assert.False(session.CanStop);
    }

    [Fact]
    public void TryBeginRun_EntersRunning_AndBlocksASecondRun()
    {
        var session = new ScriptSessionModel(new[] { "JGS" });
        int stateChanges = 0;
        session.StateChanged += (_, _) => stateChanges++;

        Assert.True(session.TryBeginRun("JGS"));
        Assert.Equal(ScriptSessionState.Running, session.State);
        Assert.Equal("JGS", session.RunningLanguage);
        Assert.True(session.CanStop);
        Assert.False(session.CanRun("JGS"));
        Assert.False(session.TryBeginRun("JGS")); // one run at a time

        session.EndRun();
        Assert.Equal(ScriptSessionState.Idle, session.State);
        Assert.Null(session.RunningLanguage);
        Assert.Equal(2, stateChanges);

        session.EndRun(); // idempotent when already idle
        Assert.Equal(2, stateChanges);
    }

    [Fact]
    public void DocumentModel_MapsLanguageFromExtension()
    {
        Assert.Equal("JGS", ScriptDocumentModel.LanguageForFile("a.jgs"));
        Assert.Equal("C#", ScriptDocumentModel.LanguageForFile(@"C:\x\a.csx"));
        Assert.Equal("C#", ScriptDocumentModel.LanguageForFile("a.cs"));
        Assert.Equal("Python", ScriptDocumentModel.LanguageForFile("a.PY"));
        Assert.Equal("JGS", ScriptDocumentModel.LanguageForFile(null)); // unsaved default

        // Non-script files open as plain text: viewable, never runnable, no highlighting.
        Assert.Equal("Text", ScriptDocumentModel.LanguageForFile("a.txt"));
        Assert.Equal("Text", ScriptDocumentModel.LanguageForFile("notes.md"));
        Assert.Equal("Text", ScriptDocumentModel.LanguageForFile("data.json"));
        Assert.Equal("Text", ScriptDocumentModel.LanguageForFile("image.png"));
    }

    [Fact]
    public void DocumentModel_TracksDirtyState_AcrossEditsAndSaves()
    {
        var model = new ScriptDocumentModel(@"C:\ws\script.jgs", "let x = 1");
        Assert.Equal("script.jgs", model.FileName);
        Assert.Equal("JGS", model.Language);
        Assert.False(model.IsDirty);

        model.SetText("let x = 2");
        Assert.True(model.IsDirty);

        model.SetText("let x = 1"); // back to the saved text: clean again
        Assert.False(model.IsDirty);

        model.SetText("let x = 3");
        model.MarkSaved();
        Assert.False(model.IsDirty);
    }

    [Fact]
    public void DocumentModel_SaveAs_RehomesFileAndLanguage()
    {
        var model = new ScriptDocumentModel(path: null, "print(1)");
        Assert.Equal("NewScript.jgs", model.FileName);
        Assert.Equal("JGS", model.Language);

        model.SetFilePath(@"C:\ws\analysis.py");
        Assert.Equal("analysis.py", model.FileName);
        Assert.Equal("Python", model.Language);
    }

    [Theory]
    [InlineData("JGS", "NewScript.jgs")]
    [InlineData("C#", "NewScript.csx")]
    [InlineData("Python", "NewScript.py")]
    [InlineData("Text", "NewScript.txt")]
    public void DocumentModel_UnsavedDocument_KeepsTheLanguageItWasCreatedAs(string language, string fileName)
    {
        // The New Script picker's whole point: highlighting and the Run engine are right immediately,
        // without waiting for a save to reveal the extension.
        var model = new ScriptDocumentModel(path: null, "// stub", language);

        Assert.Equal(language, model.Language);
        Assert.Equal(fileName, model.FileName);
    }

    [Fact]
    public void DocumentModel_ExtensionForLanguage_InvertsLanguageForFile()
    {
        foreach (string language in new[] { "JGS", "C#", "Python" })
        {
            string extension = ScriptDocumentModel.ExtensionForLanguage(language);
            Assert.Equal(language, ScriptDocumentModel.LanguageForFile("x" + extension));
        }

        // A language with no engine has no script extension of its own.
        Assert.Equal(".txt", ScriptDocumentModel.ExtensionForLanguage("Text"));
        Assert.Equal(".txt", ScriptDocumentModel.ExtensionForLanguage(null));
    }

    [Fact]
    public void DocumentModel_SavingUnderAnotherExtension_BeatsTheChosenLanguage()
    {
        // A file's extension is the durable truth; a Python tab saved as .jgs really is JGS now.
        var model = new ScriptDocumentModel(path: null, "print(1)", "Python");
        Assert.Equal("Python", model.Language);

        model.SetFilePath(@"C:\ws\ported.jgs");
        Assert.Equal("JGS", model.Language);
    }
}
