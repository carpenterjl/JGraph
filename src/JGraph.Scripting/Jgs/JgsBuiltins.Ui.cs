using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// The app-building verbs (app-building plan). U1 brings <c>uicontrol</c>: a component in a figure,
/// placed in pixels, with R2025b's properties, refusals and callback forms. Its parent is a figure;
/// panels and button groups arrive in U2 and U3.
/// </summary>
internal static partial class JgsBuiltins
{
    private static void RegisterUiBuiltins(JgsEnvironment env)
    {
        // AutoCallsBare, because the documented spelling is the bare name on an assignment's right
        // side — h = uicontrol — and a bare name in expression position is otherwise the function.
        env.Builtins.Register("uicontrol", JgsValue.Function(new BuiltinFunction("uicontrol", UiControl)
        {
            AutoCallsBare = true,
            BindsAnsAsStatement = false,
        }));
    }

    /// <summary>
    /// <c>uicontrol</c>, <c>uicontrol(parent)</c>, <c>uicontrol(___, Name, Value)</c> and
    /// <c>uicontrol(parent, s)</c> with a struct of properties — R2025b's forms, and its refusals for
    /// the others (measured in U1).
    /// </summary>
    private static JgsValue UiControl(IReadOnlyList<JgsValue> args, int line, int col)
    {
        int start = 0;
        FigureModel? parent = null;
        bool positionalParent = false;
        if (args.Count > 0 && args[0].Type is JgsType.Number or JgsType.Array && !args[0].IsStringArray)
        {
            parent = ComponentParent(args[0], hasOptions: args.Count > 1, line, col);
            positionalParent = true;
            start = 1;
        }

        // The options: name-value pairs, or one struct whose fields are the names.
        var options = new List<(string Name, JgsValue Value)>();
        if (args.Count - start == 1 && args[start].Type == JgsType.Struct && !args[start].IsStructArray)
        {
            foreach ((string name, JgsValue value) in args[start].AsStruct)
            {
                options.Add((name, value));
            }
        }
        else
        {
            if ((args.Count - start) % 2 != 0)
            {
                throw positionalParent
                    ? new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidArgs",
                        "Incorrect number of input arguments.")
                    : new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidConvenienceArgHandle",
                        "First argument must be a valid parent, such as a Figure or Panel object.");
            }

            for (int i = start; i < args.Count; i += 2)
            {
                if (!IsTextScalar(args[i]))
                {
                    throw new JgsRuntimeException(line, col, "MATLAB:hgbuiltins:object_creation:InvalidArgs",
                        "Incorrect number of input arguments.");
                }

                options.Add((TextOf(args[i]), args[i + 1]));
            }
        }

        // 'Parent' among the options says where the control goes before anything else is set.
        foreach ((string name, JgsValue value) in options)
        {
            if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
            {
                parent = ComponentParent(value, hasOptions: true, line, col);
            }
        }

        parent ??= JG.CurrentFigureNumber > 0 && JG.TryGetFigure(JG.CurrentFigureNumber, out FigureModel current)
            ? current
            : JG.Figure();

        var control = new UiControlModel();
        parent.Components.Add(control);
        JgsValue handle = JgsHandleRegistry.For(control);
        JgsHandleEntry entry = JgsHandleRegistry.EntryFor(control);
        bool fireCreate = false;
        try
        {
            using (JgsGraphicsProperties.CreatingComponent())
            {
                foreach ((string name, JgsValue value) in options)
                {
                    if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    JgsGraphicsProperties.Set(entry, name, value, line, col);
                    fireCreate |= name.Equals("CreateFcn", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // A refused option means no control, as in R2025b — not one half made.
            using (GraphObjectLifecycle.SuppressNotifications())
            {
                parent.Components.Remove(control);
            }

            throw;
        }

        JG.TouchFigure(parent);
        if (fireCreate)
        {
            JgsCallbackDispatcher.Current?.FireCreateFcn(control);
        }

        return handle;
    }

    /// <summary>
    /// The figure a component is made in, from a handle a script named, with R2025b's refusals for
    /// what is not a handle and for what cannot hold a component.
    /// </summary>
    private static FigureModel ComponentParent(JgsValue value, bool hasOptions, int line, int col)
    {
        if (!JgsHandleRegistry.TryGet(value, out JgsHandleEntry? named))
        {
            throw new JgsRuntimeException(line, col, "MATLAB:hg:dt_conv:Matrix_to_HObject:BadHandle", "Value must be a handle.");
        }

        return named.Target switch
        {
            FigureModel figure => figure,
            UiObject when hasOptions => throw new JgsRuntimeException(line, col,
                "MATLAB:hgbuiltins:object_creation:CannotSpecifyPVPairsWithNonParentConvenienceArg",
                "Invalid input combination. Parameter-value pairs must be specified with a valid parent, such as a Figure or Panel object."),
            _ => throw new JgsRuntimeException(line, col, "MATLAB:gbtobjects:Component",
                $"{JgsGraphicsCallbackValues.ClassWord(named.Target)} cannot be a parent."),
        };
    }
}
