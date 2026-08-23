using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M80's first block: the tiled layout, and the <c>Layout</c> name every object in one answers with.
/// <para>
/// <c>Layout</c> has been unanswered since M73, on the axes and then on the four pieces of furniture
/// M78 served — a deliberate ceiling, because it names a cell in a tiled layout and this build had no
/// tiled layout to name a cell of. It had <c>tiledlayout</c>, but as three integers in a closure: no
/// object, so nothing for <c>t.TileSpacing</c> or <c>ax.Layout.Tile</c> to be. M80 makes it an
/// object, and the ceiling goes with the reason for it.
/// </para>
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static void AddTiledLayoutBlock(IDictionary<string, GraphicsProperty> table)
    {
        static TiledLayoutModel Grid(JgsHandleEntry entry) => (TiledLayoutModel)entry.Target;

        Put(table, "GridSize",
            entry => Row(Grid(entry).Rows, Grid(entry).Columns),
            (entry, value, line, col) =>
            {
                double[] given = Numbers("GridSize", value, 2, line, col);
                TiledLayoutModel grid = Grid(entry);
                grid.Rows = JgsBuiltins.WholeNumber("GridSize", given[0], line, col);
                grid.Columns = JgsBuiltins.WholeNumber("GridSize", given[1], line, col);

                // A grid told its shape is no longer choosing it, which is what 'flow' means.
                grid.Flow = false;
                grid.Arrange();
            });

        AddWordProperty(table, "TileSpacing",
            entry => Grid(entry).TileSpacing.ToString().ToLowerInvariant(),
            (entry, word, line, col) =>
            {
                Grid(entry).TileSpacing = word switch
                {
                    "loose" => TileSpacingMode.Loose,
                    "compact" => TileSpacingMode.Compact,
                    "tight" => TileSpacingMode.Tight,
                    "none" => TileSpacingMode.None,
                    _ => throw new JgsRuntimeException(line, col,
                        $"TileSpacing is 'loose', 'compact', 'tight' or 'none', but got '{word}'."),
                };
                Grid(entry).Arrange();
            });

        AddWordProperty(table, "Padding",
            entry => Grid(entry).Padding.ToString().ToLowerInvariant(),
            (entry, word, line, col) =>
            {
                Grid(entry).Padding = word switch
                {
                    "loose" => TilePaddingMode.Loose,
                    "compact" => TilePaddingMode.Compact,
                    "tight" => TilePaddingMode.Tight,
                    _ => throw new JgsRuntimeException(line, col,
                        $"Padding is 'loose', 'compact' or 'tight', but got '{word}'."),
                };
                Grid(entry).Arrange();
            });

        AddWordProperty(table, "TileIndexing",
            entry => Grid(entry).TileIndexing == TileIndexingMode.ColumnMajor
                ? "columnmajor"
                : "rowmajor",
            (entry, word, line, col) =>
            {
                Grid(entry).TileIndexing = word switch
                {
                    "rowmajor" => TileIndexingMode.RowMajor,
                    "columnmajor" => TileIndexingMode.ColumnMajor,
                    _ => throw new JgsRuntimeException(line, col,
                        $"TileIndexing is 'rowmajor' or 'columnmajor', but got '{word}'."),
                };
                Grid(entry).Arrange();
            });

        // The four pieces of text a layout carries over the whole grid rather than over one tile.
        // Each reserves a band, so writing one moves every tile — which is why they lay out again.
        AddLayoutText(table, "Title", entry => Grid(entry).Title, (grid, text) => grid.Title = text);
        AddLayoutText(table, "Subtitle",
            entry => Grid(entry).Subtitle, (grid, text) => grid.Subtitle = text);
        AddLayoutText(table, "XLabel", entry => Grid(entry).XLabel, (grid, text) => grid.XLabel = text);
        AddLayoutText(table, "YLabel", entry => Grid(entry).YLabel, (grid, text) => grid.YLabel = text);

        Put(table, "Children", entry => HandleRow([.. Grid(entry).Tiles]));

        // A layout sits in a figure rather than in another layout, so its own Layout is empty and its
        // rectangle is the figure's own — the two names MATLAB gives every container.
        Put(table, "Position", entry => FlipRow(Grid(entry).Bounds),
            (entry, value, line, col) =>
            {
                double[] box = Numbers("Position", value, 4, line, col);
                Grid(entry).Bounds = FlipRect(new Rect2D(box[0], box[1], box[2], box[3]));
                Grid(entry).Arrange();
            });
        Put(table, "InnerPosition", entry => FlipRow(Grid(entry).Bounds));
        Put(table, "OuterPosition", entry => FlipRow(Grid(entry).Bounds));
        AddWordProperty(table, "PositionConstraint",
            static _ => "outerposition",
            static (_, word, line, col) =>
            {
                if (word is not ("outerposition" or "innerposition"))
                {
                    throw new JgsRuntimeException(line, col,
                        $"PositionConstraint is 'outerposition' or 'innerposition', but got '{word}'.");
                }
            });

        AddWordProperty(table, "Units",
            static _ => "normalized",
            static (_, word, line, col) =>
            {
                if (!word.Equals("normalized", StringComparison.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"A layout is placed in fractions of the figure here, and '{word}' is not a "
                        + "unit this build measures in.");
                }
            });

        // Whether the grid was told its shape or chooses it. This is the flag 'flow' sets, under the
        // name MATLAB gives it — one piece of state, two spellings, as every mode word here is.
        AddWordProperty(table, "TileArrangement",
            entry => Grid(entry).Flow ? "flow" : "fixed",
            (entry, word, line, col) =>
            {
                Grid(entry).Flow = word switch
                {
                    "flow" => true,
                    "fixed" => false,
                    _ => throw new JgsRuntimeException(
                        line, col, $"TileArrangement is 'fixed' or 'flow', but got '{word}'."),
                };
                Grid(entry).Arrange();
            });

        // A layout carries a toolbar in MATLAB and is given none by default there either, so the
        // empty answer is the faithful one. Writing it is refused: the strip this build draws hovers
        // over an axes, and a layout is not one.
        Put(table, "Toolbar",
            static _ => JgsValue.Array([]),
            static (_, _, line, col) => throw new JgsRuntimeException(line, col,
                "A toolbar here hovers over an axes, and a layout is not one — set it on a tile."));

        // A layout inside a layout is the one thing MATLAB nests that this build does not, so this
        // answers empty rather than a handle — recorded, and the same shape as an axes outside a grid.
        Put(table, "Layout", static _ => JgsValue.Array([]));
    }

    private static void AddLayoutText(
        IDictionary<string, GraphicsProperty> table,
        string name,
        Func<JgsHandleEntry, string> read,
        Action<TiledLayoutModel, string> write)
    {
        string spelling = name;
        Put(table, spelling,
            entry => JgsValue.Str(read(entry)),
            (entry, value, line, col) =>
            {
                var grid = (TiledLayoutModel)entry.Target;
                write(grid, JgsBuiltins.TitleText(spelling, value, line, col));
                grid.Arrange();
            });
    }

    /// <summary>
    /// The gestures an axes answers to without a tool being chosen. Reading gives the handles;
    /// writing replaces the whole list, which is how MATLAB's own documentation writes it —
    /// <c>ax.Interactions = [panInteraction zoomInteraction]</c>.
    /// </summary>
    private static void AddInteractionsBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Interactions",
            entry =>
            {
                var axes = (AxesModel)entry.Target;
                return axes.InteractionsDisabled
                    ? JgsValue.Array([])
                    : HandleList([.. axes.Interactions]);
            },
            (entry, value, line, col) =>
            {
                var axes = (AxesModel)entry.Target;
                var chosen = new List<InteractionModel>();
                foreach (JgsValue handle in Handles(value))
                {
                    JgsHandleEntry named = JgsHandleRegistry.Require(handle, line, col);
                    if (named.Target is not InteractionModel interaction)
                    {
                        throw new JgsRuntimeException(line, col,
                            "Interactions is a list of interaction objects — panInteraction, "
                            + "zoomInteraction, dataTipInteraction and the rest.");
                    }

                    chosen.Add(interaction);
                }

                axes.Interactions.Clear();
                foreach (InteractionModel interaction in chosen)
                {
                    axes.Interactions.Add(interaction);
                }

                // Naming a list is asking for those gestures, so it also undoes a disable — otherwise
                // the write would be remembered and not obeyed, which is the failure this whole wave
                // is about.
                axes.InteractionsDisabled = false;
            });
    }

    /// <summary>
    /// What one interaction object answers to: the one setting MATLAB documents on it, and nothing
    /// else. An interaction is a switch with a setting, and inventing more of it than MATLAB
    /// documents would be inventing behaviour a script could not have asked for.
    /// </summary>
    private static void AddInteractionBlock(Type type, IDictionary<string, GraphicsProperty> table)
    {
        if (typeof(DirectionalInteractionModel).IsAssignableFrom(type))
        {
            AddWordProperty(table, "Dimensions",
                entry => ((DirectionalInteractionModel)entry.Target).Dimensions switch
                {
                    InteractionDimensions.X => "x",
                    InteractionDimensions.Y => "y",
                    _ => "xy",
                },
                (entry, word, line, col) =>
                    ((DirectionalInteractionModel)entry.Target).Dimensions = word switch
                    {
                        "xy" => InteractionDimensions.XY,
                        "x" => InteractionDimensions.X,
                        "y" => InteractionDimensions.Y,
                        _ => throw new JgsRuntimeException(
                            line, col, $"Dimensions is 'xy', 'x' or 'y', but got '{word}'."),
                    });
        }

        if (typeof(DataTipInteractionModel).IsAssignableFrom(type))
        {
            // A tip here is always pinned to a point: the placement walks the data to find the
            // nearest one, and there is no reading of a click that puts a tip between two.
            Put(table, "SnapToDataVertex",
                static _ => JgsValue.Str("on"),
                static (_, value, line, col) =>
                {
                    if (!ToOnOff("SnapToDataVertex", value, line, col))
                    {
                        throw new JgsRuntimeException(line, col,
                            "A data tip is pinned to a data point here, so SnapToDataVertex cannot be "
                            + "turned off — a tip between two points would name a reading nobody took.");
                    }
                });
        }
    }

    /// <summary>
    /// The toolbar over an axes, and the buttons on it. Everything else a toolbar answers to is the
    /// callback and identity block every object shares, which it gets for being a graph object.
    /// </summary>
    private static void AddToolbarBlock(IDictionary<string, GraphicsProperty> table)
    {
        Put(table, "Children",
            entry => HandleList([.. ((AxesToolbarModel)entry.Target).Buttons]));

        // The one callback MATLAB documents on a toolbar. It rides the same queue every other
        // callback does, so a button pressed in the window reaches the script at its next safe point.
        AddCallbackSlot(table, "SelectionChangedFcn",
            static entry => entry.SelectionChangedFcn,
            static (entry, value) => entry.SelectionChangedFcn = value);
    }

    /// <summary>One button of that toolbar.</summary>
    private static void AddToolbarButtonBlock(IDictionary<string, GraphicsProperty> table)
    {
        static AxesToolbarButtonModel Button(JgsHandleEntry entry) =>
            (AxesToolbarButtonModel)entry.Target;

        // MATLAB's Icon can be a picture as well as a name. A picture is refused rather than kept:
        // this build draws the named buttons it knows, and one it could not draw would be a button
        // that answers its own icon and shows nothing.
        Put(table, "Icon",
            entry => JgsValue.Str(Button(entry).Icon),
            (entry, value, line, col) =>
            {
                if (value.Type != JgsType.String)
                {
                    throw new JgsRuntimeException(line, col,
                        "Icon names one of this build's buttons — "
                        + $"{string.Join(", ", AxesToolbarModel.KnownButtons)} — rather than a picture.");
                }

                string word = value.AsString.ToLowerInvariant();
                if (!AxesToolbarModel.KnownButtons.Contains(word, StringComparer.OrdinalIgnoreCase))
                {
                    throw new JgsRuntimeException(line, col,
                        $"There is no '{word}' button. This build knows "
                        + $"{string.Join(", ", AxesToolbarModel.KnownButtons)}.");
                }

                Button(entry).Icon = word;
            });

        AddWordProperty(table, "Style",
            entry => Button(entry).Style == ToolbarButtonStyle.State ? "state" : "push",
            (entry, word, line, col) => Button(entry).Style = word switch
            {
                "push" => ToolbarButtonStyle.Push,
                "state" => ToolbarButtonStyle.State,
                _ => throw new JgsRuntimeException(
                    line, col, $"Style is 'push' or 'state', but got '{word}'."),
            });

        Put(table, "Tooltip",
            entry => JgsValue.Str(Button(entry).Tooltip),
            (entry, value, line, col) => Button(entry).Tooltip =
                JgsBuiltins.StrOf("Tooltip", value, line, col));

        Put(table, "Value",
            entry => OnOff(Button(entry).Value),
            (entry, value, line, col) =>
            {
                if (Button(entry).Style != ToolbarButtonStyle.State)
                {
                    throw new JgsRuntimeException(line, col,
                        "Only a state button holds a value; a push one is up between presses.");
                }

                Button(entry).Value = ToOnOff("Value", value, line, col);
            });
    }

    /// <summary>An array of handles, or a single one, as the list a script meant.</summary>
    private static IEnumerable<JgsValue> Handles(JgsValue value) => value.Type == JgsType.Array
        ? value.BoxedElements()
        : [value];

    /// <summary>
    /// The two properties MATLAB puts on an <c>ax.Layout</c>: which cell, and how many it covers.
    /// The span is a pair because MATLAB writes it as one.
    /// </summary>
    private static void AddTilePlaceBlock(IDictionary<string, GraphicsProperty> table)
    {
        static TiledLayoutOptionsModel Place(JgsHandleEntry entry) =>
            (TiledLayoutOptionsModel)entry.Target;

        Put(table, "Tile",
            entry => JgsValue.Number(Place(entry).Tile),
            (entry, value, line, col) => Place(entry).Tile = JgsBuiltins.WholeNumber(
                "Tile", Numbers("Tile", value, 1, line, col)[0], line, col));

        Put(table, "TileSpan",
            entry => Row(Place(entry).RowSpan, Place(entry).ColumnSpan),
            (entry, value, line, col) =>
            {
                double[] given = Numbers("TileSpan", value, 2, line, col);
                TiledLayoutOptionsModel place = Place(entry);
                place.RowSpan = JgsBuiltins.WholeNumber("TileSpan", given[0], line, col);
                place.ColumnSpan = JgsBuiltins.WholeNumber("TileSpan", given[1], line, col);
            });
    }

    /// <summary>
    /// Where an object sits in a tiled layout. An axes answers with the small object MATLAB reaches
    /// its tile through; everything an axes owns answers with its axes' answer, which is the same
    /// re-pointing the layout family already does for a legend's rectangle.
    /// </summary>
    private static void AddLayoutHandle(
        IDictionary<string, GraphicsProperty> table, Func<JgsHandleEntry, AxesModel?> owner)
    {
        Func<JgsHandleEntry, AxesModel?> whose = owner;
        Put(table, "Layout",
            entry => whose(entry) is { LayoutTile: not null } axes
                ? JgsHandleRegistry.For(axes.LayoutOptions)
                : JgsValue.Array([]));
    }
}
