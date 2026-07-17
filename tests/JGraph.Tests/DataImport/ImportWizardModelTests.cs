using JGraph.Data.Import;
using Xunit;

namespace JGraph.Tests.DataImport;

public class ImportWizardModelTests
{
    private static ImportWizardModel Loaded(string text)
    {
        var model = new ImportWizardModel();
        model.LoadClipboardText(text);
        return model;
    }

    [Fact]
    public void LoadClipboardText_DefaultsXToDateAndYToRemainingNumbers()
    {
        ImportWizardModel model = Loaded("t,a,b\n2024-01-01,1,2\n2024-01-02,3,4");

        Assert.Null(model.Error);
        Assert.Equal("t", model.XColumn);
        Assert.Equal(new[] { "a", "b" }, model.YColumns);
    }

    [Fact]
    public void LoadClipboardText_NumericOnly_DefaultsXToFirstNumberColumn()
    {
        ImportWizardModel model = Loaded("x,y,z\n1,2,3\n4,5,6");
        Assert.Equal("x", model.XColumn);
        Assert.Equal(new[] { "y", "z" }, model.YColumns);
    }

    [Fact]
    public void OptionChange_Reparses_AndResetsMapping()
    {
        ImportWizardModel model = Loaded("x,y,z\n1,2,3\n4,5,6");
        model.SetYColumnSelected("z", false);
        Assert.Equal(new[] { "y" }, model.YColumns);

        model.HasHeader = true; // differs from the auto (null) value → triggers a reparse
        Assert.Equal(new[] { "y", "z" }, model.YColumns); // mapping reset to defaults
    }

    [Fact]
    public void AllowedPlotKinds_MultipleNumericY_AllowsLineScatterStemHistogram()
    {
        ImportWizardModel model = Loaded("x,y,z\n1,2,3\n4,5,6");
        Assert.Contains(ImportPlotKind.Line, model.AllowedPlotKinds);
        Assert.Contains(ImportPlotKind.Scatter, model.AllowedPlotKinds);
        Assert.Contains(ImportPlotKind.Stem, model.AllowedPlotKinds);
        Assert.Contains(ImportPlotKind.Histogram, model.AllowedPlotKinds);
        Assert.DoesNotContain(ImportPlotKind.Bar, model.AllowedPlotKinds); // 2 Y columns
        Assert.DoesNotContain(ImportPlotKind.ErrorBar, model.AllowedPlotKinds);
    }

    [Fact]
    public void AllowedPlotKinds_SingleY_AllowsBarAndErrorBar()
    {
        ImportWizardModel model = Loaded("x,y,z\n1,2,3\n4,5,6");
        model.SetYColumnSelected("z", false); // Y = {y}, spare number column z exists
        Assert.Contains(ImportPlotKind.Bar, model.AllowedPlotKinds);
        Assert.Contains(ImportPlotKind.ErrorBar, model.AllowedPlotKinds);
    }

    [Fact]
    public void PlotKind_SnapsWhenNoLongerAllowed()
    {
        ImportWizardModel model = Loaded("x,y,z\n1,2,3\n4,5,6");
        model.SetYColumnSelected("z", false); // 1 Y → ErrorBar allowed
        model.PlotKind = ImportPlotKind.ErrorBar;

        model.SetYColumnSelected("z", true); // back to 2 Y → ErrorBar disallowed
        Assert.NotEqual(ImportPlotKind.ErrorBar, model.PlotKind);
        Assert.Contains(model.PlotKind, model.AllowedPlotKinds);
    }

    [Fact]
    public void ErrorBar_CanBuild_RequiresErrorColumn()
    {
        ImportWizardModel model = Loaded("x,y,z\n1,2,3\n4,5,6");
        model.SetYColumnSelected("z", false); // Y = {y}
        model.PlotKind = ImportPlotKind.ErrorBar;

        Assert.False(model.CanBuild); // no error column yet
        model.ErrorColumn = "z";
        Assert.True(model.CanBuild);

        TablePlotSpec spec = model.BuildSpec();
        Assert.Equal(ImportPlotKind.ErrorBar, spec.Kind);
        Assert.Equal("z", spec.ErrorColumn);
        Assert.Equal(new[] { "y" }, spec.YColumns);
    }

    [Fact]
    public void BuildSpec_DefaultLine_CarriesMapping()
    {
        ImportWizardModel model = Loaded("t,a,b\n2024-01-01,1,2\n2024-01-02,3,4");
        Assert.True(model.CanBuild);

        TablePlotSpec spec = model.BuildSpec();
        Assert.Equal("t", spec.XColumn);
        Assert.Equal(new[] { "a", "b" }, spec.YColumns);
    }

    [Fact]
    public void BadInput_SetsError_WithoutThrowing()
    {
        var model = new ImportWizardModel();
        model.LoadClipboardText(string.Empty);

        Assert.NotNull(model.Error);
        Assert.Null(model.Result);
        Assert.False(model.CanBuild);
    }

    [Fact]
    public void LoadFile_DispatchesByExtension()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"jgraph_wiz_{Guid.NewGuid():N}.csv");
        string xlsxPath = Path.Combine(Path.GetTempPath(), $"jgraph_wiz_{Guid.NewGuid():N}.xlsx");
        try
        {
            File.WriteAllText(csvPath, "x,y\n1,2\n3,4");
            File.WriteAllBytes(xlsxPath, new XlsxFixture()
                .Sheet("S", new[] { XCell.Text("x") }, new[] { XCell.Number(1) })
                .BuildStream().ToArray());

            var csvModel = new ImportWizardModel();
            csvModel.LoadFile(csvPath);
            Assert.Equal(ImportSourceKind.DelimitedFile, csvModel.SourceKind);

            var xlsxModel = new ImportWizardModel();
            xlsxModel.LoadFile(xlsxPath);
            Assert.Equal(ImportSourceKind.XlsxFile, xlsxModel.SourceKind);
            Assert.Equal(new[] { "S" }, xlsxModel.SheetNames);
        }
        finally
        {
            File.Delete(csvPath);
            File.Delete(xlsxPath);
        }
    }
}
