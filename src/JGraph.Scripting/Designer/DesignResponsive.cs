using System.Globalization;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Designer;

/// <summary>
/// App Designer's responsive apps, its "2-Panel App with Auto-Reflow" and "3-Panel App with
/// Auto-Reflow" (designer plan DS4b, read from R2025b's <c>ResponsiveAppTemplateData</c>,
/// <c>ResponsiveAppFactory</c>, <c>AppPropertiesBlockBehavior</c>, <c>ReadonlyCallbackCodeBehavior</c>,
/// <c>FigureUtil</c> and MATLAB's <c>MLAPPResponsiveAppValidator</c>): a figure holding one grid of one
/// row and two or three columns, a panel in each - the regions, named by their column: leading,
/// centre, trailing - and the code App Designer writes for it, which it never lets the user edit: a
/// private properties block of the widths the layout changes at, and the figure's
/// <c>SizeChangedFcn</c> callback <c>updateAppLayout</c>, which rearranges the regions by the figure's
/// width. App Designer writes that callback again from its template whenever it opens or saves the
/// app, from the regions' sizes; so does the designer.
/// </summary>
public static class DesignResponsive
{
    /// <summary>The comment over the private properties block of a responsive app.</summary>
    public const string PropertiesComment = "% Properties that correspond to apps with auto-reflow";

    /// <summary>The comment over the generated callback; it names the figure <c>UIFigure</c> whatever it is called.</summary>
    public const string CallbackComment = "% Changes arrangement of the app based on UIFigure width";

    /// <summary>The comment over an app's callbacks block.</summary>
    private const string CallbacksComment = "% Callbacks that handle component events";

    /// <summary>The comment over an app's createComponents block.</summary>
    private const string InitializationComment = "% Component initialization";

    /// <summary>The generated callback's name.</summary>
    public const string Callback = "updateAppLayout";

    /// <summary>The figure width at or under which every region is stacked, one to a row.</summary>
    public const double OnePanelWidth = 576;

    /// <summary>The figure width at or under which a 3-panel app has two rows.</summary>
    public const double TwoPanelWidth = 768;

    /// <summary>A new app's side regions' width.</summary>
    public const double SideWidth = 220;

    /// <summary>
    /// The grid a responsive app's figure holds (App Designer's "responsive container"); null for an
    /// app that is not responsive.
    /// </summary>
    public static DesignComponent? Grid(AppDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        return design.IsResponsive
            ? design.Figure.Children.FirstOrDefault(static c => c.Type == DesignDocument.GridType)
            : null;
    }

    /// <summary>
    /// How many regions the app's template has (<c>FigureUtil.getTemplateName</c>): 2 or 3 for a grid
    /// of one row and two or three columns; 0 when the app is not responsive or its grid is neither,
    /// which App Designer opens as a standard app.
    /// </summary>
    public static int Panels(AppDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (Grid(design) is not { } grid)
        {
            return 0;
        }

        int rows = Tracks(grid.Get("RowHeight")).Count;
        int columns = Tracks(grid.Get("ColumnWidth")).Count;
        return rows == 1 && columns is 2 or 3 ? columns : 0;
    }

    /// <summary>
    /// A grid's rows or columns as the design holds them, one value to a track: a cell's items, a
    /// numeric row's numbers (<c>[220 420]</c>, which the grid takes as pixels), a single text; the
    /// grid's own two <c>'1x'</c> tracks when the design sets none.
    /// </summary>
    public static IReadOnlyList<DesignValue> Tracks(DesignValue? value) => value switch
    {
        CellValue cell => cell.Items,
        NumericValue numbers => [.. numbers.Data.Select(static n => (DesignValue)NumericValue.Scalar(n))],
        null => [new CharValue("1x"), new CharValue("1x")],
        _ => [value],
    };

    /// <summary>
    /// The regions, by their column (<c>LayoutTemplateStateGenerator</c>): the panels in the grid, the
    /// leading region first; null where a column has none.
    /// </summary>
    public static IReadOnlyList<DesignComponent?> Regions(AppDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        int panels = Panels(design);
        var regions = new DesignComponent?[panels];
        if (Grid(design) is { } grid)
        {
            foreach (DesignComponent child in grid.Children.Where(static c => c.Type == "matlab.ui.container.Panel"))
            {
                if (GridSpan.FromValues(child.Get("Layout.Row"), child.Get("Layout.Column")) is { } span
                    && span.ColumnStart == span.ColumnEnd && span.ColumnStart >= 1 && span.ColumnStart <= panels)
                {
                    regions[span.ColumnStart - 1] ??= child;
                }
            }
        }

        return regions;
    }

    /// <summary>
    /// Whether the responsive app is laid out as one of App Designer's templates, so that its regions
    /// can be split: its figure's grid has one row and two or three columns
    /// (<c>FigureUtil.getTemplateName</c>, <c>MLAPPResponsiveAppValidator</c>) with a panel in each. Only
    /// an edit of its code can make a responsive app anything else; it stays responsive, its grid
    /// locked, as App Designer keeps the type its file says.
    /// </summary>
    public static bool IsTemplate(AppDesign design) => Panels(design) > 0 && Regions(design).All(static r => r is not null);

    /// <summary>Whether <paramref name="component"/> is one of a responsive app's regions.</summary>
    public static bool IsRegion(AppDesign design, DesignComponent component)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(component);
        return Regions(design).Contains(component);
    }

    /// <summary>The private properties block of the widths the layout changes at, as App Designer writes it.</summary>
    public static IReadOnlyList<string> PropertiesBlock(int panels, DesignCodeStyle? style = null)
    {
        style ??= DesignCodeStyle.Default;
        var lines = new List<string>
        {
            style.Indent(1) + PropertiesComment,
            style.Indent(1) + "properties (Access = private)",
            style.Indent(2) + "onePanelWidth = " + Number(OnePanelWidth) + ";",
        };
        if (panels == 3)
        {
            lines.Add(style.Indent(2) + "twoPanelWidth = " + Number(TwoPanelWidth) + ";");
        }

        lines.Add(style.Indent(1) + "end");
        return lines;
    }

    /// <summary>
    /// The lines of the generated callback's body, before the indentation of a function's body: App
    /// Designer's template for the app's regions, its tokens given the regions' sizes - every region's
    /// height the figure's, the side regions' widths their columns' - as it gives them from the
    /// design-time geometry (<c>ResponsiveAppFactory._adjustRegionPanelData</c>). Null when the app is
    /// not a responsive one with its regions in place.
    /// </summary>
    /// <param name="design">The app.</param>
    /// <param name="sizeOf">A region's size as the canvas lays it out, for a column whose width is not pixels; null when there is no canvas.</param>
    public static IReadOnlyList<string>? Body(AppDesign design, Func<DesignComponent, Size2D?>? sizeOf = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        int panels = Panels(design);
        IReadOnlyList<DesignComponent?> regions = Regions(design);
        if (panels == 0 || regions.Any(static r => r is null) || Grid(design) is not { } grid)
        {
            return null;
        }

        double height = design.Figure.Get("Position") is NumericValue { Data.Count: 4 } position ? position.Data[3] : 480;
        IReadOnlyList<DesignValue> columns = Tracks(grid.Get("ColumnWidth"));
        string Width(int column) =>
            columns.ElementAtOrDefault(column) is NumericValue { Data: [var pixels] } ? Number(pixels)
            : sizeOf?.Invoke(regions[column]!) is { } size ? Number(size.Width)
            : Number(SideWidth);

        string figure = design.Figure.CodeName;
        string g = grid.CodeName;
        string left = regions[0]!.CodeName;
        string right = regions[^1]!.CodeName;
        string h = Number(height);
        if (panels == 2)
        {
            return
            [
                $"currentFigureWidth = app.{figure}.Position(3);",
                "if(currentFigureWidth <= app.onePanelWidth)",
                "    % Change to a 2x1 grid",
                $"    app.{g}.RowHeight = {{{h}, {h}}};",
                $"    app.{g}.ColumnWidth = {{'1x'}};",
                $"    app.{right}.Layout.Row = 2;",
                $"    app.{right}.Layout.Column = 1;",
                "else",
                "    % Change to a 1x2 grid",
                $"    app.{g}.RowHeight = {{'1x'}};",
                $"    app.{g}.ColumnWidth = {{{Width(0)}, '1x'}};",
                $"    app.{right}.Layout.Row = 1;",
                $"    app.{right}.Layout.Column = 2;",
                "end",
            ];
        }

        string centre = regions[1]!.CodeName;
        return
        [
            $"currentFigureWidth = app.{figure}.Position(3);",
            "if(currentFigureWidth <= app.onePanelWidth)",
            "    % Change to a 3x1 grid",
            $"    app.{g}.RowHeight = {{{h}, {h}, {h}}};",
            $"    app.{g}.ColumnWidth = {{'1x'}};",
            $"    app.{centre}.Layout.Row = 1;",
            $"    app.{centre}.Layout.Column = 1;",
            $"    app.{left}.Layout.Row = 2;",
            $"    app.{left}.Layout.Column = 1;",
            $"    app.{right}.Layout.Row = 3;",
            $"    app.{right}.Layout.Column = 1;",
            "elseif (currentFigureWidth > app.onePanelWidth && currentFigureWidth <= app.twoPanelWidth)",
            "    % Change to a 2x2 grid",
            $"    app.{g}.RowHeight = {{{h}, {h}}};",
            $"    app.{g}.ColumnWidth = {{'1x', '1x'}};",
            $"    app.{centre}.Layout.Row = 1;",
            $"    app.{centre}.Layout.Column = [1,2];",
            $"    app.{left}.Layout.Row = 2;",
            $"    app.{left}.Layout.Column = 1;",
            $"    app.{right}.Layout.Row = 2;",
            $"    app.{right}.Layout.Column = 2;",
            "else",
            "    % Change to a 1x3 grid",
            $"    app.{g}.RowHeight = {{'1x'}};",
            $"    app.{g}.ColumnWidth = {{{Width(0)}, '1x', {Width(2)}}};",
            $"    app.{left}.Layout.Row = 1;",
            $"    app.{left}.Layout.Column = 1;",
            $"    app.{centre}.Layout.Row = 1;",
            $"    app.{centre}.Layout.Column = 2;",
            $"    app.{right}.Layout.Row = 1;",
            $"    app.{right}.Layout.Column = 3;",
            "end",
        ];
    }

    /// <summary>
    /// <paramref name="text"/>, a responsive app's class, with what App Designer generates for it
    /// written again from <paramref name="design"/>: the private properties block of its widths and
    /// the body of the figure's <c>SizeChangedFcn</c> callback, its signature and comment - put back
    /// last in the callbacks block when the code has lost it. Every other line is kept, and so are the
    /// text's line endings. An app that is not a responsive template is returned as it is, and so are
    /// the callbacks of text whose functions are not all closed.
    /// </summary>
    public static string Splice(string text, AppDesign design, DesignCodeStyle? style = null, Func<DesignComponent, Size2D?>? sizeOf = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(design);
        style ??= DesignCodeStyle.Default;
        int panels = Panels(design);
        if (panels == 0 || Body(design, sizeOf) is not { } body)
        {
            return text;
        }

        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        // The callback's body first: it comes after the properties block, whose length may change.
        // The callback is found as the code copy's layout finds it, by its block structure; text caught
        // mid-edit, whose functions cannot be told apart, keeps its callbacks as they are.
        string callback = design.Figure.Get("SizeChangedFcn") is CallbackValue bound ? bound.Method : Callback;
        JGraph.Scripting.Jgs.JgsMlappLayout layout = JGraph.Scripting.Jgs.JgsMlappLayout.Of(text);
        if (layout is { IsAppLayout: true, IsComplete: true })
        {
            if (layout.Callbacks.FirstOrDefault(r => r.Name == callback) is { } region)
            {
                // Its body, signature and comment as App Designer writes them.
                int signature = region.FirstLine - 1;
                string indent = lines[signature][..(lines[signature].Length - lines[signature].TrimStart().Length)];
                lines.RemoveRange(region.FirstLine, region.LineCount);
                lines.InsertRange(region.FirstLine, body.Select(l => indent + style.Indent(1) + l));
                lines[signature] = indent + $"function {callback}(app, event)";
                if (signature > 0 && lines[signature - 1].TrimStart().StartsWith('%'))
                {
                    lines[signature - 1] = indent + CallbackComment;
                }
                else
                {
                    lines.Insert(signature, indent + CallbackComment);
                }
            }
            else if (!lines.Exists(l => FunctionNamed(l, callback)))
            {
                PutBack(lines, layout, style, callback, body);
            }
        }

        int comment = lines.FindIndex(static l => l.Trim() == PropertiesComment);
        if (comment >= 0 && comment + 1 < lines.Count && lines[comment + 1].Trim().StartsWith("properties", StringComparison.Ordinal))
        {
            int blockEnd = lines.FindIndex(comment + 2, static l => l.Trim() == "end");
            if (blockEnd > comment)
            {
                lines.RemoveRange(comment, blockEnd - comment + 1);
                lines.InsertRange(comment, PropertiesBlock(panels, style));
            }
        }

        return string.Join(newline, lines);
    }

    /// <summary>
    /// Puts a layout callback the code has lost back, last in the callbacks block, where App Designer
    /// keeps its read-only one (<c>_updateAddedCallbackOrder</c>) and never lets it be deleted (its
    /// template's <c>IsDeletable</c> is false); in a block of its own before createComponents' when
    /// the app has no callbacks.
    /// </summary>
    /// <param name="lines">The class's lines, as <paramref name="layout"/> numbers them.</param>
    /// <param name="layout">The class's layout, complete, which says where its callbacks are.</param>
    /// <param name="style">The app's code style.</param>
    /// <param name="callback">The callback's name.</param>
    /// <param name="body">Its body.</param>
    private static void PutBack(List<string> lines, JGraph.Scripting.Jgs.JgsMlappLayout layout, DesignCodeStyle style, string callback, IReadOnlyList<string> body)
    {
        string[] method =
        [
            style.Indent(2) + CallbackComment,
            style.Indent(2) + $"function {callback}(app, event)",
            .. body.Select(l => style.Indent(3) + l),
            style.Indent(2) + "end",
        ];

        // After the last callback's end, as the layout found it by its blocks.
        if (layout.Callbacks.LastOrDefault() is { } last)
        {
            lines.InsertRange(last.FirstLine + last.LineCount + 1, [string.Empty, .. method]);
            return;
        }

        // An empty callbacks block (the layout found its comment and its methods line), or none.
        int initialization = lines.FindIndex(static l => l.Trim() == InitializationComment);
        int block = lines.FindIndex(static l => l.Trim() == CallbacksComment);
        if (block >= 0 && block < initialization)
        {
            // Under the methods line after an empty line, and no second empty line after it.
            int at = block + 2;
            lines.InsertRange(at, [string.Empty, .. method]);
            int after = at + 1 + method.Length;
            if (after < lines.Count && string.IsNullOrWhiteSpace(lines[after]))
            {
                lines.RemoveAt(after);
            }
        }
        else if (initialization >= 0)
        {
            lines.InsertRange(initialization,
                [style.Indent(1) + CallbacksComment, style.Indent(1) + "methods (Access = private)", string.Empty, .. method, style.Indent(1) + "end", string.Empty]);
        }
    }

    /// <summary>Whether <paramref name="line"/> begins a function called <paramref name="name"/>, with or without outputs.</summary>
    private static bool FunctionNamed(string line, string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(line, $@"^\s*function\s+(?:\[[^\]]*\]\s*=\s*|\w+\s*=\s*)?{System.Text.RegularExpressions.Regex.Escape(name)}\b");

    /// <summary>
    /// The text of a new responsive app as App Designer makes one (<c>ResponsiveAppFactory</c> with
    /// <c>ResponsiveAppTemplateData</c>): a figure 640 (2 panels) or 860 (3) by 480 that resizes its
    /// regions itself, its grid of one row - 220 pixels for each side region, the rest for the centre -
    /// with no padding or spacing, scrolling; the panels <c>LeftPanel</c>, <c>CenterPanel</c> (3 panels)
    /// and <c>RightPanel</c>; the widths block; and <c>updateAppLayout</c>. The component code is the
    /// designer's own writer's.
    /// </summary>
    public static string NewAppText(int panels)
    {
        if (panels is not (2 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(panels), panels, "A responsive app has 2 or 3 panels.");
        }

        DesignCatalog catalog = DesignCatalog.R2025b;
        DesignComponent Make(string codeName, string type) =>
            new(codeName, catalog.ByType(type) ?? throw new InvalidOperationException($"The catalog has no {type}."));

        DesignComponent figure = Make("UIFigure", "matlab.ui.Figure");
        figure.Set("AutoResizeChildren", OnOffValue.Off);
        figure.Set("Position", NumericValue.Row(100, 100, panels == 2 ? 640 : 860, 480));
        figure.Set("Name", new CharValue("MATLAB App"));
        figure.Set("SizeChangedFcn", new CallbackValue(Callback));
        var design = new AppDesign(figure) { IsResponsive = true };

        DesignComponent grid = Make("GridLayout", DesignDocument.GridType);
        grid.Set("ColumnWidth", panels == 2
            ? CellValue.Row(NumericValue.Scalar(SideWidth), new CharValue("1x"))
            : CellValue.Row(NumericValue.Scalar(SideWidth), new CharValue("1x"), NumericValue.Scalar(SideWidth)));
        grid.Set("RowHeight", CellValue.Row(new CharValue("1x")));
        grid.Set("ColumnSpacing", NumericValue.Scalar(0));
        grid.Set("RowSpacing", NumericValue.Scalar(0));
        grid.Set("Padding", NumericValue.Row(0, 0, 0, 0));
        grid.Set("Scrollable", OnOffValue.On);
        figure.Add(grid);

        string[] names = panels == 2 ? ["LeftPanel", "RightPanel"] : ["LeftPanel", "CenterPanel", "RightPanel"];
        for (int i = 0; i < names.Length; i++)
        {
            DesignComponent panel = Make(names[i], "matlab.ui.container.Panel");
            panel.Set("Layout.Row", NumericValue.Scalar(1));
            panel.Set("Layout.Column", NumericValue.Scalar(i + 1));
            grid.Add(panel);
        }

        // The widths block and the layout callback are the class writer's to write for a template (DS5).
        var code = new DesignCode("app1");
        code.Callbacks.Add(new DesignCallback(Callback, isGenerated: true));
        return DesignClassWriter.Render(design, code);
    }

    /// <summary>A number as App Designer's code engine prints one (JavaScript's <c>String(n)</c>): 220, 220.5.</summary>
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
