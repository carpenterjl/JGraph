using JGraph.Api;
using JGraph.Core.Model;
using JGraph.Core.Primitives;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// Containers, units and the screen (app-building plan, U2): the <c>uipanel</c>'s property surface,
/// <c>Units</c> on figures, axes and components, the root's real screen values, and the pieces of the
/// tree every handle verb shares. Words, coercions and refusals are R2025b's, measured headless in
/// <c>tools/matlab-checklist/ui-probes/u2</c>.
/// </summary>
internal static partial class JgsGraphicsProperties
{
    private static readonly string[] BorderTypeWords = ["none", "line", "etchedin", "etchedout", "beveledin", "beveledout"];
    private static readonly string[] BorderTypeShown = ["none", "line"];
    private static readonly string[] TitlePositionWords =
        ["lefttop", "centertop", "righttop", "leftbottom", "centerbottom", "rightbottom"];

    private static readonly string[] HandleVisibilityWords = ["on", "callback", "off"];

    private static UiPanelModel Panel(JgsHandleEntry entry) => (UiPanelModel)entry.Target;

    /// <summary>The primary screen's size in pixels: what a figure's normalized units are fractions of.</summary>
    internal static Size2D ScreenExtent()
    {
        Rect2D primary = UiScreen.Primary;
        return new Size2D(primary.Width, primary.Height);
    }

    /// <summary>
    /// A warning from a property write, for <c>lastwarn</c> and the command window. A write made
    /// where no script is running (a test poking the table) has nowhere to say it, and says nothing.
    /// </summary>
    private static void PropertyWarning(string identifier, string text)
    {
        if (JgsCallbackDispatcher.Current?.Interpreter?.Host is { } host)
        {
            JgsBuiltins.Warn(host, identifier, text);
        }
    }

    // --- the tree ------------------------------------------------------------------------------

    /// <summary>
    /// The children a script sees: <see cref="ChildrenOf"/> less the ones whose handles are hidden,
    /// in the same back-to-front order.
    /// </summary>
    internal static IReadOnlyList<GraphObject> VisibleChildrenOf(GraphObject target)
    {
        var shown = new List<GraphObject>();
        foreach (GraphObject child in ChildrenOf(target))
        {
            if (!JgsHandleRegistry.TryGetEntry(child, out JgsHandleEntry? entry) || entry.HandleVisible)
            {
                shown.Add(child);
            }
        }

        return shown;
    }

    /// <summary>What MATLAB's <c>Parent</c> answers: an axes placed in a panel belongs to the panel.</summary>
    internal static GraphObject? ParentOf(GraphObject target) =>
        target is AxesModel { Container: { } container } ? container : target.Parent;

    /// <summary>
    /// <c>set(h, 'Children', order)</c>: a permutation of the children, front first. Components and
    /// axes are two stacks — a component is always above an axes — so each is reordered among its own
    /// kind; an order that puts an axes above a component changes nothing and warns, as R2025b's does.
    /// </summary>
    private static void SetChildren(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        IReadOnlyList<GraphObject> current = ChildrenOf(entry.Target);
        var wanted = new List<GraphObject>();
        int count = value.Type == JgsType.Array ? value.ArrayLength : 1;
        for (int i = 0; i < count; i++)
        {
            JgsValue element = value.Type == JgsType.Array ? value.ElementAt(i) : value;
            if (!JgsHandleRegistry.TryGet(element, out JgsHandleEntry? child))
            {
                throw new JgsRuntimeException(line, col, "MATLAB:hg:BadChildren",
                    "Children must be either be a valid double array or an object array.");
            }

            wanted.Add(child.Target);
        }

        // Hidden children are not in what a script read, so they are not expected in what it writes.
        var shown = new HashSet<GraphObject>(VisibleChildrenOf(entry.Target), ReferenceEqualityComparer.Instance);
        if (wanted.Count != shown.Count || wanted.Distinct(ReferenceEqualityComparer.Instance).Count() != wanted.Count
            || !wanted.All(shown.Contains))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:hg:BadChildrenPermutation",
                "Children may only be set to a permutation of itself.");
        }

        if (entry.Target is not IUiContainer holder)
        {
            throw new JgsRuntimeException(line, col,
                $"The order of a {entry.TypeName}'s children cannot be set in this build.");
        }

        bool seenAxes = false;
        foreach (GraphObject child in wanted)
        {
            seenAxes |= child is AxesModel;
            if (seenAxes && child is UiObject)
            {
                PropertyWarning("MATLAB:hg:default_child_strategy:IllegalPermutation", "Illegal permutation. No effect on view");
                return;
            }
        }

        // Front first in the script's list; back first in the model's.
        var components = wanted.OfType<UiObject>().Reverse().ToList();
        var axes = wanted.OfType<AxesModel>().Reverse().ToList();
        using (GraphObjectLifecycle.SuppressNotifications())
        {
            Restack(holder.Components, components);
            if (AxesHolder(entry.Target) is { } figure)
            {
                Restack(figure.Axes, axes);
            }
        }
    }

    /// <summary>Puts the named items of a collection in the given order, in the slots they hold.</summary>
    internal static void Restack<T>(GraphObjectCollection<T> collection, IReadOnlyList<T> ordered)
        where T : GraphObject
    {
        var slots = new List<int>();
        for (int i = 0; i < collection.Count; i++)
        {
            if (ordered.Contains(collection[i]))
            {
                slots.Add(i);
            }
        }

        // Moves rather than removals, so nothing is announced as deleted and no parent link drops.
        for (int k = 0; k < slots.Count; k++)
        {
            int from = collection.IndexOf(ordered[k]);
            if (from != slots[k])
            {
                collection.Move(from, slots[k]);
            }
        }

        if (slots.Count > 0)
        {
            collection[slots[0]].Invalidate(InvalidationKind.Ui);
        }
    }

    /// <summary>The figure whose axes list holds the axes of a figure or a container.</summary>
    internal static FigureModel? AxesHolder(GraphObject target) => target switch
    {
        FigureModel figure => figure,
        UiObject component => component.Figure,
        _ => null,
    };

    /// <summary>
    /// Moves a component or an axes into a figure or a container — a move, never a deletion. The
    /// newcomer goes to the front of its kind, as R2025b's does, and keeps its Position numbers.
    /// </summary>
    private static bool TryReparentIntoContainer(JgsHandleEntry entry, JgsHandleEntry owner, int line, int col)
    {
        if (entry.Target is not (UiObject or AxesModel))
        {
            return false;
        }

        string moving = JgsGraphicsCallbackValues.ClassWord(entry.Target);
        if (owner.Target is not IUiContainer holder)
        {
            throw entry.Target is AxesModel
                ? new JgsRuntimeException(line, col, "MATLAB:handle_graphics:exceptions:HandleGraphicsException",
                    $"{moving} cannot be a child of {JgsGraphicsCallbackValues.ClassWord(owner.Target)}.")
                : owner.Target is JgsGraphicsRoot
                    ? new JgsRuntimeException(line, col,
                        entry.Target is UiControlModel ? "MATLAB:uicontrol:InvalidParent" : "MATLAB:uicontainer:InvalidParentFigure",
                        entry.Target is UiControlModel
                            ? "Parent must be a Figure or UITab or any UIContainer"
                            : "Parent must be a Figure or any UIContainer")
                    : new JgsRuntimeException(line, col, "MATLAB:gbtobjects:Component",
                        $"{JgsGraphicsCallbackValues.ClassWord(owner.Target)} cannot be a parent.");
        }

        if (entry.Target is UiContainerModel container && owner.Target is UiObject into && container.Holds(into))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:container:InvalidParentToChild",
                "You are trying to set the parent property to a descendant child object.");
        }

        FigureModel? newFigure = AxesHolder(owner.Target);
        using (GraphObjectLifecycle.SuppressNotifications())
        {
            switch (entry.Target)
            {
                case UiObject component:
                {
                    FigureModel? oldFigure = component.Figure;
                    List<AxesModel> held = component is UiContainerModel moved && oldFigure is not null
                        ? [.. oldFigure.Axes.Where(moved.Holds)]
                        : [];
                    component.Container?.Components.Remove(component);
                    holder.Components.Add(component);

                    // The axes placed in a panel live in its figure's list, so they follow it to another.
                    if (newFigure is not null && !ReferenceEquals(oldFigure, newFigure))
                    {
                        foreach (AxesModel axes in held)
                        {
                            UiContainerModel? keep = axes.Container;
                            oldFigure!.Axes.Remove(axes);
                            newFigure.Axes.Add(axes);
                            axes.Container = keep;
                        }
                    }

                    break;
                }

                case AxesModel axes when newFigure is not null:
                    (axes.Parent as FigureModel)?.Axes.Remove(axes);
                    newFigure.Axes.Add(axes);
                    axes.Container = owner.Target as UiContainerModel;
                    break;
            }
        }

        if (newFigure is not null)
        {
            JG.TouchFigure(newFigure);
        }

        return true;
    }

    // --- units ---------------------------------------------------------------------------------

    private static UiUnits UnitsWord(JgsHandleEntry entry, JgsValue value, int line, int col) =>
        (UiUnits)Word(entry, "Units", value, UnitWords, UnitWords, line, col);

    private static JgsValue UnitsValue(UiUnits units) => JgsValue.Str(UnitWords[(int)units]);

    /// <summary>A figure's rectangle on the screen, as MATLAB's pixel rectangle.</summary>
    internal static Rect2D FigurePixels(FigureModel figure) =>
        new(figure.Position.X, figure.Position.Y, figure.Size.Width, figure.Size.Height);

    /// <summary>Places a figure by MATLAB's pixel rectangle. A window is never less than a pixel.</summary>
    internal static void SetFigurePixels(FigureModel figure, Rect2D pixels)
    {
        // As one request, which a window acts on whatever it had: by the time it does, Size may
        // hold the viewport the window still had, and the corner may not have moved at all.
        figure.RequestPlacement(
            new Point2D(pixels.X, pixels.Y),
            new Size2D(System.Math.Max(1, pixels.Width), System.Math.Max(1, pixels.Height)));
    }

    /// <summary>
    /// Notes the size a figure or a container has now, as the size its next resize is measured
    /// from — what <c>AutoResizeChildren</c> scales by and what decides whether a
    /// <c>SizeChangedFcn</c> is due.
    /// </summary>
    internal static void RememberSize(GraphObject target)
    {
        Size2D size = target switch
        {
            FigureModel figure => figure.Size,
            UiObject component => new Size2D(component.PixelPosition().Width, component.PixelPosition().Height),
            _ => default,
        };
        JgsHandleRegistry.EntryFor(target).LastSize = (size.Width, size.Height);
    }

    private static void AddFigureUnits(IDictionary<string, GraphicsProperty> table)
    {
        foreach (string name in new[] { "Position", "InnerPosition", "OuterPosition" })
        {
            string captured = name;
            Put(table, captured,
                entry =>
                {
                    FigureModel figure = Figure(entry);

                    // The window's bounds with its title bar and border, when a window is there to
                    // ask. Headless there is no chrome, and R2025b answers the drawable area too.
                    Rect2D pixels = captured == "OuterPosition"
                        && ScriptGraphicsCallbacks.WindowBoundsProvider?.Invoke(figure) is { } outer
                            ? outer
                            : FigurePixels(figure);
                    return RectRow(UiUnitConverter.FromPixels(pixels, figure.Units, ScreenExtent()));
                },
                (entry, value, line, col) =>
                {
                    FigureModel figure = Figure(entry);
                    Rect2D box = ComponentPosition(entry, captured, value, line, col);
                    SetFigurePixels(figure, UiUnitConverter.ToPixels(box, figure.Units, ScreenExtent()));
                });
        }

        Put(table, "Units",
            entry => UnitsValue(Figure(entry).Units),
            (entry, value, line, col) => Figure(entry).Units = UnitsWord(entry, value, line, col));

        Put(table, "AutoResizeChildren",
            entry => OnOff(Figure(entry).AutoResizeChildren),
            (entry, value, line, col) =>
            {
                bool on = ComponentOnOff(entry, "AutoResizeChildren", value, line, col);
                Figure(entry).AutoResizeChildren = on;
                WarnIfResizeCallbackIsSilenced(entry, on);
            });

        Put(table, "Scrollable",
            entry => OnOff(Figure(entry).Scrollable),
            (entry, value, line, col) => Figure(entry).Scrollable = ComponentOnOff(entry, "Scrollable", value, line, col));

        Put(table, "IntegerHandle",
            entry => OnOff(Figure(entry).IntegerHandle),
            (entry, value, line, col) =>
            {
                FigureModel figure = Figure(entry);
                bool on = ComponentOnOff(entry, "IntegerHandle", value, line, col);
                if (on == figure.IntegerHandle)
                {
                    return;
                }

                if (on && JG.IsHiddenNumber(JG.GetFigureNumber(figure)))
                {
                    throw new JgsRuntimeException(line, col,
                        "A uifigure cannot be given a figure number in this build, so its IntegerHandle stays 'off'.");
                }

                figure.IntegerHandle = on;
                if (on)
                {
                    JgsHandleRegistry.ForgetMinted(figure);
                }
            });

        Put(table, "Number", entry =>
        {
            FigureModel figure = Figure(entry);
            return figure.IntegerHandle
                ? JgsValue.Number(JG.GetFigureNumber(figure))
                : JgsMatrix.FromColumnMajor([], 0, 0);
        });

        foreach (string name in new[] { "SizeChangedFcn", "ResizeFcn" })
        {
            AddResizeSlot(table, name);
        }
    }

    /// <summary>
    /// <c>SizeChangedFcn</c> (and its older name) on a figure or a container: the usual slot, with
    /// R2025b's warning when <c>AutoResizeChildren</c> is on, which silences it.
    /// </summary>
    private static void AddResizeSlot(IDictionary<string, GraphicsProperty> table, string name)
    {
        AddCallbackSlot(table, name, static entry => entry.SizeChangedFcn, static (entry, value) => entry.SizeChangedFcn = value);
        GraphicsProperty slot = table[name];
        Put(table, name, slot.Read, (entry, value, line, col) =>
        {
            slot.Write!(entry, value, line, col);
            WarnIfResizeCallbackIsSilenced(entry, entry.Target is IUiContainer { AutoResizeChildren: true });
        });
    }

    private static void WarnIfResizeCallbackIsSilenced(JgsHandleEntry entry, bool autoResize)
    {
        if (autoResize && entry.SizeChangedFcn is not null)
        {
            PropertyWarning("MATLAB:ui:containers:SizeChangedFcnDisabledWhenAutoResizeOn",
                "'SizeChangedFcn' callback will not execute while 'AutoResizeChildren' is set to 'on'.");
        }
    }

    // --- axes ----------------------------------------------------------------------------------

    /// <summary>
    /// An axes' plot box or cell as MATLAB's pixel rectangle in the area it is placed in. A rectangle
    /// pinned in absolute units is answered as pinned; the other one, and both while normalized, come
    /// from where the axes is drawn.
    /// </summary>
    internal static Rect2D AxesPixels(AxesModel axes, bool inner)
    {
        if (axes.PixelBounds is { } pinned && inner == (axes.InnerTarget is not null))
        {
            return pinned;
        }

        Size2D area = axes.ReferenceSize();
        Rect2D fraction;
        if (axes.PixelBounds is null)
        {
            fraction = inner ? InnerOf(axes) : OuterOf(axes);
        }
        else
        {
            AxesLayoutSnapshot layout = LayoutOf(axes);
            fraction = layout.Normalize(inner ? layout.PlotAreaPx : layout.OuterPx);
        }

        return new Rect2D(
            (fraction.X * area.Width) + 1,
            ((1 - fraction.Y - fraction.Height) * area.Height) + 1,
            fraction.Width * area.Width,
            fraction.Height * area.Height);
    }

    /// <summary>MATLAB's pixel rectangle as the downward-Y fractions the model keeps.</summary>
    private static Rect2D FractionOf(Rect2D pixels, Size2D area) => area.Width <= 0 || area.Height <= 0
        ? new Rect2D(0, 0, 1, 1)
        : new Rect2D(
            (pixels.X - 1) / area.Width,
            1 - (((pixels.Y - 1) + pixels.Height) / area.Height),
            pixels.Width / area.Width,
            pixels.Height / area.Height);

    private static JgsValue AxesRectValue(AxesModel axes, bool inner)
    {
        if (axes.Units == UiUnits.Normalized)
        {
            return FlipRow(inner ? InnerOf(axes) : OuterOf(axes));
        }

        return RectRow(UiUnitConverter.FromPixels(AxesPixels(axes, inner), axes.Units, axes.ReferenceSize()));
    }

    private static void SetAxesRect(AxesModel axes, Rect2D value, bool inner)
    {
        Rect2D fraction;
        Rect2D? pinned = null;
        if (axes.Units == UiUnits.Normalized)
        {
            fraction = FlipRect(value);
        }
        else
        {
            Size2D area = axes.ReferenceSize();
            pinned = UiUnitConverter.ToPixels(value, axes.Units, area);
            fraction = FractionOf(pinned.Value, area);
        }

        if (inner)
        {
            axes.InnerTarget = fraction;
            axes.PositionConstraint = PositionConstraintType.InnerPosition;
        }
        else
        {
            axes.InnerTarget = null;
            axes.PositionConstraint = PositionConstraintType.OuterPosition;
            axes.NormalizedBounds = fraction;
        }

        axes.PixelBounds = pinned;
    }

    /// <summary>
    /// A script's <c>Units</c> write on an axes: it stays where it is. Leaving normalized pins the
    /// rectangle it is placed by in pixels, so a resize no longer moves it; coming back turns those
    /// pixels into fractions of the area as it is now.
    /// </summary>
    internal static void SetAxesUnits(AxesModel axes, UiUnits units)
    {
        if (units == axes.Units)
        {
            return;
        }

        Size2D area = axes.ReferenceSize();
        if (axes.Units == UiUnits.Normalized)
        {
            Rect2D fraction = axes.InnerTarget ?? axes.NormalizedBounds;
            axes.PixelBounds = new Rect2D(
                (fraction.X * area.Width) + 1,
                ((1 - fraction.Y - fraction.Height) * area.Height) + 1,
                fraction.Width * area.Width,
                fraction.Height * area.Height);
        }
        else if (units == UiUnits.Normalized && axes.PixelBounds is not null)
        {
            Rect2D fraction = axes.PlacementIn(area);
            if (axes.InnerTarget is not null)
            {
                axes.InnerTarget = fraction;
            }
            else
            {
                axes.NormalizedBounds = fraction;
            }

            axes.PixelBounds = null;
        }

        axes.Units = units;
    }

    // --- the root ------------------------------------------------------------------------------

    private static void AddRootBlock(IDictionary<string, GraphicsProperty> table)
    {
        // The real screens (U2), in the root's own Units: pixels count from 1 and every other unit
        // from 0, exactly as a component's position does.
        Put(table, "ScreenSize", entry =>
            RectRow(UiUnitConverter.FromPixels(UiScreen.Primary, ((JgsGraphicsRoot)entry.Target).Units, ScreenExtent())));
        Put(table, "MonitorPositions", entry =>
        {
            IReadOnlyList<Rect2D> monitors = UiScreen.Monitors;
            var data = new double[monitors.Count * 4];
            for (int i = 0; i < monitors.Count; i++)
            {
                Rect2D box = UiUnitConverter.FromPixels(monitors[i], ((JgsGraphicsRoot)entry.Target).Units, ScreenExtent());
                data[i] = box.X;
                data[monitors.Count + i] = box.Y;
                data[(2 * monitors.Count) + i] = box.Width;
                data[(3 * monitors.Count) + i] = box.Height;
            }

            return JgsMatrix.FromColumnMajor(data, monitors.Count, 4);
        });
        Put(table, "ScreenPixelsPerInch", static _ => JgsValue.Number(96));
        Put(table, "Units",
            entry => UnitsValue(((JgsGraphicsRoot)entry.Target).Units),
            (entry, value, line, col) => ((JgsGraphicsRoot)entry.Target).Units = UnitsWord(entry, value, line, col));
        Put(table, "ShowHiddenHandles",
            static _ => OnOff(JgsHandleRegistry.ShowHiddenHandles),
            (entry, value, line, col) =>
                JgsHandleRegistry.ShowHiddenHandles = ComponentOnOff(entry, "ShowHiddenHandles", value, line, col));

        // Empty when no figure is current: asking must not make one, as gcf would.
        Put(table, "CurrentFigure", static _ =>
            JG.CurrentFigureNumberOrZero > 0 && JG.TryGetFigure(JG.CurrentFigureNumberOrZero, out FigureModel current)
                ? JgsHandleRegistry.For(current)
                : JgsMatrix.FromColumnMajor([], 0, 0));
        Put(table, "Children", static _ => HandleRow(RootFigures(hidden: false)));
    }

    /// <summary>The figures the root lists: every one, or only those whose handles show.</summary>
    internal static IReadOnlyList<GraphObject> RootFigures(bool hidden)
    {
        var figures = new List<GraphObject>();
        foreach (int number in JG.FigureNumbers)
        {
            if (JG.TryGetFigure(number, out FigureModel figure)
                && (hidden || !JgsHandleRegistry.TryGetEntry(figure, out JgsHandleEntry? entry) || entry.HandleVisible))
            {
                figures.Add(figure);
            }
        }

        return figures;
    }

    // --- uipanel -------------------------------------------------------------------------------

    private static void AddUiPanelBlock(IDictionary<string, GraphicsProperty> table)
    {
        // A panel has no TooltipString (the older spelling is a uicontrol's alone).
        table.Remove("TooltipString");

        Put(table, "Title",
            entry => TextValue(Panel(entry).Title),
            (entry, value, line, col) => Panel(entry).Title = ComponentText(entry, "Title", value, line, col));

        Put(table, "TitlePosition",
            entry => JgsValue.Str(TitlePositionWords[(int)Panel(entry).TitlePosition]),
            (entry, value, line, col) => Panel(entry).TitlePosition =
                (UiTitlePosition)Word(entry, "TitlePosition", value, TitlePositionWords, TitlePositionWords, line, col));

        Put(table, "BorderType",
            entry => JgsValue.Str(BorderTypeWords[(int)Panel(entry).BorderType]),
            (entry, value, line, col) =>
            {
                int word = Word(entry, "BorderType", value, BorderTypeWords, BorderTypeShown, line, col);
                Panel(entry).BorderType = (UiBorderType)word;
                if (word > 1)
                {
                    PropertyWarning("MATLAB:Uipanel:UnsupportedBorderType",
                        $"BorderType will not accept {BorderTypeWords[word]} in a future release. Use \"line\" or \"none\" instead.");
                }
            });

        Put(table, "BorderWidth",
            entry => JgsValue.Number(Panel(entry).BorderWidth),
            (entry, value, line, col) =>
            {
                double width = NumericScalar(entry, "BorderWidth", value, line, col);
                Panel(entry).BorderWidth = width < 0
                    ? throw new JgsRuntimeException(line, col, "MATLAB:hg:uipanel", "Border width cannot have negative values.")
                    : width;
            });

        Put(table, "BackgroundColor",
            entry => UiColorValue(Panel(entry).BackgroundColor),
            (entry, value, line, col) => Panel(entry).BackgroundColor =
                ComponentColor(entry, "BackgroundColor", value, line, col)
                ?? throw new JgsRuntimeException(line, col, "MATLAB:hg:ColorSpec_None",
                    "Cannot set Panel BackgroundColor to 'none'."));
        Put(table, "ForegroundColor",
            entry => UiColorValue(Panel(entry).ForegroundColor),
            (entry, value, line, col) => Panel(entry).ForegroundColor = ComponentColor(entry, "ForegroundColor", value, line, col));
        Put(table, "BorderColor",
            entry => UiColorValue(Panel(entry).BorderColor),
            (entry, value, line, col) => Panel(entry).BorderColor = ComponentColor(entry, "BorderColor", value, line, col));
        Put(table, "HighlightColor",
            entry => UiColorValue(Panel(entry).HighlightColor),
            (entry, value, line, col) => Panel(entry).HighlightColor = ComponentColor(entry, "HighlightColor", value, line, col));
        Put(table, "ShadowColor",
            entry => UiColorValue(Panel(entry).ShadowColor),
            (entry, value, line, col) =>
            {
                if (ComponentColor(entry, "ShadowColor", value, line, col) is { } shade)
                {
                    Panel(entry).ShadowColor = shade;
                }

                PropertyWarning("MATLAB:Uipanel:UnsupportedProperty",
                    "ShadowColor will be removed in a future release. Use BorderColor instead.");
            });

        Put(table, "FontName",
            entry => JgsValue.Str(Panel(entry).FontName),
            (entry, value, line, col) => Panel(entry).FontName = FontNameOf(entry, value, line, col));
        Put(table, "FontSize",
            entry => JgsValue.Number(Panel(entry).FontSize),
            (entry, value, line, col) => Panel(entry).FontSize = FontSizeOf(entry, value, line, col));
        Put(table, "FontUnits",
            entry => JgsValue.Str(FontUnitWords[(int)Panel(entry).FontUnits]),
            (entry, value, line, col) =>
            {
                // A change of units keeps the size the title is drawn at: FontSize is re-expressed.
                UiPanelModel panel = Panel(entry);
                var units = (UiFontUnits)Word(entry, "FontUnits", value, FontUnitWords, FontUnitWords, line, col);
                double pixels = panel.FontSizeInPixels();
                panel.FontUnits = units;
                double one = panel.FontSizeInPixels() / panel.FontSize;
                panel.FontSize = one > 0 && double.IsFinite(one) ? pixels / one : panel.FontSize;
            });
        Put(table, "FontWeight",
            entry => JgsValue.Str(Panel(entry).FontWeight),
            (entry, value, line, col) => Panel(entry).FontWeight =
                FontWeightWords[Word(entry, "FontWeight", value, FontWeightWords, FontWeightShown, line, col)]);
        Put(table, "FontAngle",
            entry => JgsValue.Str(Panel(entry).FontAngle),
            (entry, value, line, col) => Panel(entry).FontAngle =
                FontAngleWords[Word(entry, "FontAngle", value, FontAngleWords, FontAngleShown, line, col)]);

        // A container is on or off; 'inactive' is a control's word.
        Put(table, "Enable",
            entry => OnOff(Panel(entry).Enable != UiEnable.Off),
            (entry, value, line, col) => Panel(entry).Enable =
                ComponentOnOff(entry, "Enable", value, line, col) ? UiEnable.On : UiEnable.Off);

        Put(table, "Scrollable",
            entry => OnOff(Panel(entry).Scrollable),
            (entry, value, line, col) => Panel(entry).Scrollable = ComponentOnOff(entry, "Scrollable", value, line, col));
        Put(table, "AutoResizeChildren",
            entry => OnOff(Panel(entry).AutoResizeChildren),
            (entry, value, line, col) =>
            {
                bool on = ComponentOnOff(entry, "AutoResizeChildren", value, line, col);
                Panel(entry).AutoResizeChildren = on;
                WarnIfResizeCallbackIsSilenced(entry, on);
            });
        Put(table, "Clipping",
            entry => OnOff(Panel(entry).Clipping),
            (entry, value, line, col) => Panel(entry).Clipping = ComponentOnOff(entry, "Clipping", value, line, col));

        // The inner area is a measurement, in the panel's own units and its parent's coordinates.
        Put(table, "InnerPosition", entry =>
        {
            UiPanelModel panel = Panel(entry);
            return RectRow(UiUnitConverter.FromPixels(panel.InnerPixelRect(), panel.Units, panel.ReferenceSize()));
        });

        foreach (string name in new[] { "SizeChangedFcn", "ResizeFcn" })
        {
            AddResizeSlot(table, name);
        }

        // The grid's placement options arrive with uigridlayout (U5); until then there are none.
        Put(table, "Layout",
            static _ => JgsMatrix.FromColumnMajor([], 0, 0),
            (entry, _, line, col) => throw ComponentError(entry, "Layout", "MATLAB:ui:datatypes:LayoutOptionsDatatype:InvalidClass",
                "'Layout' value must be specified as a matlab.ui.layout.LayoutOptions object.", line, col));
    }

    private static string FontNameOf(JgsHandleEntry entry, JgsValue value, int line, int col)
    {
        if (!JgsBuiltins.IsTextScalar(value))
        {
            throw ComponentError(entry, "FontName", "MATLAB:class:RequireString",
                "Value must be a character vector or a string scalar.", line, col);
        }

        string name = JgsBuiltins.TextOf(value);
        return name.Length > 0
            ? name
            : throw ComponentError(entry, "FontName", "MATLAB:class:MATLABConversionError",
                "Character vector value must not be empty", line, col);
    }
}
