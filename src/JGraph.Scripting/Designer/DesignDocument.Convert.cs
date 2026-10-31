using JGraph.Scripting.Workspace;

namespace JGraph.Scripting.Designer;

/// <content>
/// Convert, Save Copy As and Export to .m File (App Designer window plan W8c): <c>ResponsifyAppAction</c> over <c>AppDataCopyUtil</c>, <c>SaveCopyAsCommand</c>, <c>ExportModelCommand</c>.
/// </content>
public sealed partial class DesignDocument
{
    private bool _keepEditorUndo;

    /// <summary>Whether the code editor keeps its undo history through this render (<c>clearRTCUndoStack: false</c>, Add Function's and Add Property's); asked once.</summary>
    public bool TakeKeepEditorUndo()
    {
        bool keep = _keepEditorUndo;
        _keepEditorUndo = false;
        return keep;
    }

    /// <summary>The figure's properties a converted app keeps as its template has them (<c>copyFigureProperties</c>).</summary>
    private static readonly string[] FigurePropertiesKept = ["SizeChangedFcn", "AutoResizeChildren", "Resize", "ContextMenu"];

    /// <summary>Whether Convert can start from this app: the design holds everything its file does, and its code can be worked on.</summary>
    public bool CanConvert => ReadProblems.Count == 0 && Code is not null;

    /// <summary>Whether this app has regions, which says which way Convert goes (<c>isResponsiveApp</c>).</summary>
    public bool HasConvertibleRegions => DesignResponsive.Panels(Design) > 0;

    /// <summary>
    /// A new app, not saved, with this one's components in the regions of a <paramref name="panels"/>-panel template (<c>_convert2Column</c>, <c>_convert3Column</c>,
    /// <c>_processResponsifyConversion</c>): the figure's properties but those the template sets, each component in the region its middle falls in, what has no box left on the
    /// figure, the code, the groups whose members stay together, the code options and the details under the new name. This app is not touched.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app cannot be converted (<see cref="CanConvert"/>).</exception>
    public DesignConverted ConvertToPanels(int panels, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!CanConvert)
        {
            throw new InvalidOperationException(Code is null ? CodeProblem! : SaveRefusal);
        }

        DesignDocument from = Clone();
        DesignDocument to = FromText(DesignResponsive.NewAppText(panels, name), null);
        CopyFigure(from, to);
        Size2DPair figure = FigureSize(to.Design);
        IReadOnlyList<double> ratios = DesignConversion.Ratios(panels);
        IReadOnlyList<(double X, double Width)> boxes = DesignConversion.RegionBoxes(ratios, figure.Width);
        IReadOnlyList<DesignComponent?> regions = DesignResponsive.Regions(to.Design);
        if (DesignResponsive.Grid(to.Design) is { } grid)
        {
            grid.Set("ColumnWidth", panels == 2
                ? CellValue.Row(NumericValue.Scalar(boxes[0].Width), new CharValue("1x"))
                : CellValue.Row(NumericValue.Scalar(boxes[0].Width), new CharValue("1x"), NumericValue.Scalar(boxes[2].Width)));
        }

        var names = new HashSet<string>(to.Design.Components.Select(static c => c.CodeName), StringComparer.Ordinal);
        foreach (DesignComponent child in from.Design.Figure.Children.ToArray())
        {
            from.Design.Figure.Remove(child);
            UniqueNames(child, names);
            if (child.Get("Position") is NumericValue { Data: [var x, _, var w, _] })
            {
                // getComponentsWithinBounds: the region its middle is in; a component on a boundary goes to the first, where R2025b puts one in both.
                int region = Enumerable.Range(0, boxes.Count).FirstOrDefault(i => DesignConversion.InRegion(x, w, figure.Width, ratios[i], ratios[i + 1]), -1);
                if (region >= 0)
                {
                    regions[region]!.Add(child);
                    Place(child, -boxes[region].X, boxes[region].Width, figure.Height);
                    continue;
                }

                Place(child, 0, figure.Width, figure.Height);
            }

            to.Design.Figure.Add(child); // checkUnprocessedComponents: menus, toolbars, what no region took
        }

        string? detached = from.Code is { } code && from.CallbackOf(from.Design.Figure, "SizeChangedFcn") is { } bound && code.Find(bound) is { IsGenerated: false } ? bound : null;
        return new DesignConverted(Finish(from, to, name), detached);
    }

    /// <summary>
    /// A new app, not saved, with the regions of this one as plain panels on a blank figure (<c>_convertToStandard</c>, <c>_processStandardConversion</c>, <c>unResponsifyPanels</c>): each at its
    /// place as it lays out now, its responsive marks gone, the rest of the figure's children beside them, and the generated layout callback left behind.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app cannot be converted (<see cref="CanConvert"/>).</exception>
    public DesignConverted ConvertToStandard(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!CanConvert)
        {
            throw new InvalidOperationException(Code is null ? CodeProblem! : SaveRefusal);
        }

        DesignDocument from = Clone();
        DesignDocument to = FromText(AppDesignerDocument.NewAppText(name), null);
        CopyFigure(from, to);
        Size2DPair figure = FigureSize(to.Design);
        var names = new HashSet<string>(to.Design.Components.Select(static c => c.CodeName), StringComparer.Ordinal);
        DesignComponent? grid = DesignResponsive.Grid(from.Design);
        IReadOnlyList<DesignComponent?> regions = DesignResponsive.Regions(from.Design);
        IReadOnlyList<DesignValue> columns = grid is null ? [] : DesignResponsive.Tracks(grid.Get("ColumnWidth"));
        double fixedWidth = columns.OfType<NumericValue>().Sum(static c => c.Data.Count > 0 ? c.Data[0] : 0);
        double at = 1;
        for (int i = 0; i < regions.Count; i++)
        {
            if (regions[i] is not { } region)
            {
                continue;
            }

            double width = columns.ElementAtOrDefault(i) is NumericValue { Data: [var pixels] } ? pixels : figure.Width - fixedWidth;
            region.Parent?.Remove(region);
            region.Unset("Layout.Row");
            region.Unset("Layout.Column");
            region.Set("Position", NumericValue.Row(at, 1, width, figure.Height));
            UniqueNames(region, names);
            to.Design.Figure.Add(region);
            at += width;
        }

        foreach (DesignComponent child in from.Design.Figure.Children.Where(c => !ReferenceEquals(c, grid)).ToArray())
        {
            from.Design.Figure.Remove(child);
            UniqueNames(child, names);
            to.Design.Figure.Add(child);
        }

        return new DesignConverted(Finish(from, to, name), null);
    }

    /// <summary>A copy of this app in its format, with objects of its own.</summary>
    private DesignDocument Clone()
    {
        DesignDocument copy = Format == DesignFileFormat.PlainText ? FromPlainText(CurrentText, null) : FromText(CurrentText, null);
        copy.Design.AttachGroups(Design.GroupRecords());
        return copy;
    }

    private readonly record struct Size2DPair(double Width, double Height);

    private static Size2DPair FigureSize(AppDesign design) =>
        design.Figure.Get("Position") is NumericValue { Data: [_, _, var w, var h] } ? new Size2DPair(w, h) : new Size2DPair(640, 480);

    /// <summary><c>copyFigureProperties</c>: every property of the figure but those the template sets, and its code name.</summary>
    private static void CopyFigure(DesignDocument from, DesignDocument to)
    {
        DesignComponent source = from.Design.Figure;
        DesignComponent target = to.Design.Figure;
        foreach (KeyValuePair<string, DesignValue> property in source.Properties.ToArray())
        {
            if (!FigurePropertiesKept.Contains(property.Key, StringComparer.Ordinal))
            {
                target.Set(property.Key, property.Value);
            }
        }

        target.CodeName = source.CodeName;
    }

    /// <summary><c>_insertComponents</c>: moved by <paramref name="dx"/> at the top (<c>_offsetPositionToParent</c>), and each box brought inside its parent, all the way down (<c>clampToParent</c>).</summary>
    private static void Place(DesignComponent component, double dx, double parentWidth, double parentHeight)
    {
        double width = parentWidth;
        double height = parentHeight;
        if (component.Get("Position") is NumericValue { Data: [var x, var y, var w, var h] })
        {
            (double X, double Y, double Width, double Height) box = DesignConversion.Clamp(x + dx, y, w, h, parentWidth, parentHeight);
            component.Set("Position", NumericValue.Row(box.X, box.Y, box.Width, box.Height));
            width = box.Width;
            height = box.Height;
        }

        foreach (DesignComponent child in component.Children)
        {
            Place(child, 0, width, height);
        }
    }

    /// <summary><c>_insertComponents</c>'s name: one the new app has already gets a number.</summary>
    private static void UniqueNames(DesignComponent component, HashSet<string> names)
    {
        if (!names.Add(component.CodeName))
        {
            component.CodeName = DesignCallbacks.Unique(component.CodeName, names);
            names.Add(component.CodeName);
        }

        foreach (DesignComponent child in component.Children)
        {
            UniqueNames(child, names);
        }
    }

    /// <summary>
    /// The code, the groups and the details moved over (<c>copyCustomCode</c>, <c>createGroups</c>, <c>createCallbacks</c>, <c>copyAppSettings</c>), and the new app read again from what it
    /// comes to, so that it stands as any other opened one does, modified.
    /// </summary>
    private DesignDocument Finish(DesignDocument from, DesignDocument to, string name)
    {
        DesignCode source = from.Code!;
        DesignCode target = to.Code!;
        source.ClassName = name;
        target.HeadComments.AddRange(source.HeadComments);
        target.EditableSection.AddRange(source.EditableSection);
        target.InputParameters = source.InputParameters;
        target.SingleInstance = source.SingleInstance;
        int at = 0;
        foreach (DesignCallback callback in source.Callbacks.Where(static c => !c.IsGenerated))
        {
            target.Callbacks.Insert(at++, callback);
        }

        // createGroups: a group whose members all came over, with one parent still.
        var byName = to.Design.Components.ToDictionary(static c => c.CodeName, StringComparer.Ordinal);
        DesignGroupRecord[] records = [.. from.Design.GroupRecords().Concat(Design.GroupRecords()).DistinctBy(static r => r.Id)
            .Where(r => r.Members.All(byName.ContainsKey) && r.Members.Select(m => byName[m].Parent).Distinct().Count() == 1)];

        DesignPlainTextDetails details = DesignPlainTextDetails.NewApp(name, to.Design.IsResponsive);
        DesignAppDetailsValues held = AppDetailsValues;
        details.Values = held with { Name = name, Thumbnail = string.Empty };
        to.Format = Format;
        to.Details = details;
        to._renderedText = null;
        string text = to.CurrentText;
        DesignDocument made = Format == DesignFileFormat.PlainText ? FromPlainText(text, null) : FromText(text, null);
        made.Design.AttachGroups(records);
        made.Details = details;
        made._detailsChanged = true;
        made._textDirty = true;
        return made;
    }

    /// <summary>
    /// A copy of the app as it stands written to <paramref name="target"/> under that file's name (<c>AppModel.copy</c>, <c>MLAPPExporter</c>), in the format its extension names; this app
    /// stays as it is, its file and its modified state too.
    /// </summary>
    /// <returns>What the save did, for the status line.</returns>
    /// <exception cref="InvalidOperationException">The design cannot be saved (<see cref="CanSaveAs"/>).</exception>
    /// <exception cref="System.IO.IOException">The file could not be written, or the name is not one a class can have.</exception>
    public void SaveCopy(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!CanSaveAs)
        {
            throw new InvalidOperationException(Code is null ? CodeProblem! : SaveRefusal);
        }

        target = System.IO.Path.GetFullPath(target);
        string name = AppDesignerDocument.ClassNameFor(target);
        DesignFileFormat format = FormatOf(target);
        DesignDocument copy = Clone();
        DesignPlainTextDetails details = copy.Details ?? DesignPlainTextDetails.NewApp(name, Design.IsResponsive);
        details.Values = AppDetailsValues;
        if (details.Name == Code!.ClassName)
        {
            details.Name = name;
        }

        copy.Details = details;
        if (RunArguments.All.Count > 1)
        {
            copy._runArguments = new DesignRunArguments(RunArguments.All);
        }

        if (format == DesignFileFormat.Mlapp && Format == DesignFileFormat.Mlapp && Path is { } source && System.IO.File.Exists(source))
        {
            copy.Code!.ClassName = name;
            copy._renderedText = null;
            AppDesignerDocument.Save(source, target, copy.CurrentText, Design.GroupRecords(), MetadataOf(details), RunArguments.All);
            return;
        }

        copy.Code!.ClassName = name;
        copy.Format = format;
        copy._renderedText = null;
        if (format == DesignFileFormat.PlainText)
        {
            (copy._newline, copy._trailingNewline) = ("\n", false);
            WriteWhole(target, copy.CurrentText);
        }
        else
        {
            (copy._newline, copy._trailingNewline) = ("\n", true);
            AppDesignerDocument.SaveNew(target, copy.CurrentText, MetadataOf(details), Design.GroupRecords(), RunArguments.All);
        }
    }

    /// <summary>
    /// The app's class as a plain <c>.m</c> file under that file's name (<c>ExportModelCommand</c>, <c>MLAPPExporter</c>): the class alone, which runs without App Designer and is not reopened in it. A
    /// plain-text app's is the class a package holds, deriving from <c>matlab.apps.AppBase</c>, as that format's own has no way to make its components outside App Designer.
    /// </summary>
    /// <exception cref="System.IO.IOException">The file could not be written, or the name is not one a class can have.</exception>
    public void Export(string target)
    {
        ArgumentNullException.ThrowIfNull(target);
        string name = AppDesignerDocument.ClassNameFor(target);
        DesignDocument copy = Clone();
        DesignCode? code = copy.Code;
        if (code is null)
        {
            AppDesignerDocument.Export(target, CurrentText);
            return;
        }

        code.ClassName = name;
        copy._renderedText = null;
        string text = Format == DesignFileFormat.PlainText ? DesignClassWriter.Render(copy.Design, code, Style, SizeOfRegion) : copy.CurrentText;
        AppDesignerDocument.Export(target, text);
    }
}
