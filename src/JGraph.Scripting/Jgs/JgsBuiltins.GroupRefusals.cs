namespace JGraph.Scripting.Jgs;

/// <summary>
/// The axes verbs R2025b refuses a group or a transform for (open item 168): <c>title(g, 'x')</c>
/// is "Using title with Group is not supported.", <c>hold(g, 'on')</c> "Using hold with hggroup is
/// not supported.", <c>xlim(g, ...)</c> "Invalid or deleted graphics object.", each verb in its own
/// words (probes <c>gv2</c>, <c>gv3</c>), where this build read the group's handle as data or took
/// it as the axes. The makers take a group first (ADR 0255); these verbs speak about an axes, and a
/// group is not one.
/// <para>
/// The refusal is put round whatever each verb's registration ended up as, last of all, so every
/// door into the verb - one output, several, a statement - meets it. <c>legend(g, 'a')</c> and
/// <c>text(g, ...)</c> are taken by R2025b and are not listed; <c>cla(g)</c> is taken too, and
/// clears in a way this build does not follow (open item 168's leftovers).
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>How R2025b refuses one verb a group: the group, the argument count, and where.</summary>
    private delegate JgsRuntimeException GroupRefusal(JgsGraphicsGroup group, int count, int line, int col);

    private static GroupRefusal GroupChartRefusal(string verb) => (group, _, line, col) =>
        new(line, col, "MATLAB:Chart:UnsupportedConvenienceFunction",
            $"Using {verb} with {(group.Transforms ? "Transform" : "Group")} is not supported.");

    private static GroupRefusal GroupRefusalSaying(string identifier, string message) => (_, _, line, col) =>
        new(line, col, identifier, message);

    private static readonly GroupRefusal GroupRulerRefusal =
        GroupRefusalSaying("MATLAB:rulerFunctions:InvalidObject", "Invalid or deleted graphics object.");

    private static GroupRefusal GroupAspectRefusal(string property, string verb) => (_, count, line, col) => count == 1
        ? new(line, col, "MATLAB:hg:set_chck:Expected3ElementVector",
            $"Error setting property '{property}' of class 'Axes': Value must be a 3 element numeric vector")
        : new(line, col, $"MATLAB:{verb}:InvalidNumberArguments", "Wrong number of arguments");

    /// <summary>Every verb R2025b refuses a group or a transform first, and its words (probes gv2, gv3).</summary>
    private static IEnumerable<(string Verb, GroupRefusal Refusal)> AxesVerbGroupRefusals()
    {
        foreach (string verb in (string[])["title", "subtitle", "xlabel", "ylabel", "zlabel"])
        {
            yield return (verb, GroupChartRefusal(verb));
        }

        yield return ("axis", (group, _, line, col) => new(line, col, "MATLAB:Chart:UnsupportedConvenienceFunction",
            $"Using axis with {(group.Transforms ? "hgtransform" : "hggroup")} is not supported."));
        foreach (string verb in (string[])["hold", "ishold"])
        {
            yield return (verb, (group, _, line, col) => new(line, col, "MATLAB:hold:UnsupportedCurrentAxes",
                $"Using hold with {(group.Transforms ? "hgtransform" : "hggroup")} is not supported."));
        }

        yield return ("grid", GroupRefusalSaying("MATLAB:grid:FirstArgAxes", "First argument must be an axes handle."));

        // box(g, 'on') sets the property and names the class by its last word; box(g) reads it whole.
        yield return ("box", (group, count, line, col) => new(line, col, "MATLAB:hg:InvalidProperty",
            $"Unrecognized property Box for class {(count == 1 ? "matlab.graphics.primitive." : string.Empty)}{(group.Transforms ? "Transform" : "Group")}."));
        yield return ("colorbar", GroupRefusalSaying("MATLAB:colorbar:InvalidInput", "Unrecognized input or invalid parameter/value pair arguments."));
        foreach (string verb in (string[])[
            "xlim", "ylim", "zlim", "clim", "caxis", "rlim", "thetalim",
            "xticks", "yticks", "zticks", "xticklabels", "yticklabels", "zticklabels",
            "xtickangle", "ytickangle", "ztickangle", "xtickformat", "ytickformat", "ztickformat",
            "xscale", "yscale", "zscale"])
        {
            yield return (verb, GroupRulerRefusal);
        }

        yield return ("view", GroupRefusalSaying("MATLAB:view:InvalidInputs", "Invalid input arguments."));
        yield return ("colormap", GroupRefusalSaying("MATLAB:colormap:NeedScalarHandle", "First argument must be a scalar axes or figure handle."));
        yield return ("daspect", GroupAspectRefusal("DataAspectRatio", "daspect"));
        yield return ("pbaspect", GroupAspectRefusal("PlotBoxAspectRatio", "pbaspect"));
        yield return ("shading", GroupRefusalSaying("MATLAB:shading:NeedAxesHandle", "First argument must be an axes handle."));
        yield return ("lighting", GroupRefusalSaying("MATLAB:lighting:InputMustBeHandle", "First argument must be an axes handle."));
        foreach (string verb in (string[])["xline", "yline"])
        {
            yield return (verb, GroupRefusalSaying("MATLAB:graphics:constantline:InvalidParent", "Parent must be a Cartesian axes."));
        }

        yield return ("colororder", GroupRefusalSaying("MATLAB:graphics:colororder:InvalidTarget", "Target must be a scalar figure or axes."));
        yield return ("camlight", GroupRefusalSaying("MATLAB:camlight:InvalidNumberOfArguments", "Invalid arguments"));
        yield return ("annotation", GroupRefusalSaying("MATLAB:annotation:FirstArgFigureOrAnnotation",
            "First argument must be a valid annotation type or a handle to a figure, uipanel, or uitab."));
    }

    /// <summary>How R2025b refuses one verb a deleted object first: whether it was an axes, its class, and where.</summary>
    private delegate JgsRuntimeException DeadTargetRefusal(bool wasAxes, string? retiredClass, int line, int col);

    private static DeadTargetRefusal DeadTargetSaying(string identifier, string message) => (_, _, line, col) =>
        new(line, col, identifier, message);

    /// <summary>
    /// Every axes verb R2025b refuses a deleted group, transform or axes first, and its words (open
    /// item 313, probe p313): this build read the dead handle's number as a title, a limit or data.
    /// </summary>
    private static IEnumerable<(string Verb, DeadTargetRefusal Refusal)> AxesVerbDeadTargetRefusals()
    {
        foreach (string verb in (string[])["title", "subtitle", "xlabel", "ylabel", "zlabel"])
        {
            yield return (verb, DeadTargetSaying($"MATLAB:{verb}:InvalidTarget", "Invalid target."));
        }

        yield return ("hold", (axes, cls, line, col) => axes
            ? new(line, col, "MATLAB:graphics:axescheck:DeletedAxes", "Attempt to plot into deleted Axes.")
            : new(line, col, "MATLAB:hold:UnsupportedCurrentAxes", $"Using hold with {cls ?? "matlab.graphics.primitive.Group"} is not supported."));
        yield return ("grid", DeadTargetSaying("MATLAB:grid:FirstArgAxes", "First argument must be an axes handle."));
        yield return ("box", DeadTargetSaying("MATLAB:box:ExpectedAxesHandle", "First argument must be an axes handle."));
        yield return ("axis", (axes, _, line, col) => axes
            ? new(line, col, "MATLAB:hg:dt_conv:Matrix_to_HObject:BadHandle", "Value must be a handle.")
            : new(line, col, "MATLAB:class:InvalidHandle", "Invalid or deleted object."));

        // The ruler verbs read a first argument as R2025b's rulerFunctions does: a deleted object is
        // a graphics object that is not valid (xscale reads its own, JgsBuiltins.RulerScale).
        foreach (string verb in (string[])[
            "xlim", "ylim", "zlim", "xticks", "yticks", "zticks", "xticklabels", "yticklabels", "zticklabels",
            "xtickangle", "ytickangle", "ztickangle", "xtickformat", "ytickformat", "ztickformat"])
        {
            yield return (verb, DeadTargetSaying("MATLAB:rulerFunctions:InvalidObject", "Invalid or deleted graphics object."));
        }

        yield return ("legend", DeadTargetSaying("MATLAB:legend:InvalidGraphicsHandle", "At least one of the specified objects is invalid or deleted."));
        yield return ("cla", (axes, _, line, col) => axes
            ? new(line, col, "MATLAB:cla:InvalidAxesHandle", "Specify an axes object or array of axes objects.")
            : new(line, col, "MATLAB:clo:invalidHandle", "Bad handle"));
        yield return ("colorbar", DeadTargetSaying("MATLAB:colorbar:InvalidInput", "Unrecognized input or invalid parameter/value pair arguments."));
        yield return ("view", (axes, _, line, col) => axes
            ? new(line, col, "MATLAB:view:InvalidFirstArgument", "When specified as a graphics object, first argument must be one or more axes objects.")
            : new(line, col, "MATLAB:view:InvalidInputs", "Invalid input arguments."));
    }

    /// <summary>
    /// Puts R2025b's refusal of a deleted object first round each axes verb that has one, in a
    /// MATLAB-dialect script (open item 313). Run with <see cref="RefuseGroupsAtAxesVerbs"/>.
    /// </summary>
    private static void RefuseDeadTargetsAtAxesVerbs(JgsEnvironment env)
    {
        foreach ((string verb, DeadTargetRefusal refusal) in AxesVerbDeadTargetRefusals())
        {
            GuardAxesVerb(env, verb, (args, line, col) =>
            {
                if (args.Count > 0 && JgsRunningDialect.ThreadIsMatlab && IsDeadHandle(args[0]))
                {
                    string? cls = JgsHandleRegistry.RetiredClassOf(args[0]);
                    throw refusal(cls?.EndsWith(".Axes", StringComparison.Ordinal) == true, cls, line, col);
                }
            });
        }
    }

    /// <summary>Wraps whatever <paramref name="verb"/> is registered as so that <paramref name="refuse"/> runs first, at every door.</summary>
    private static void GuardAxesVerb(JgsEnvironment env, string verb, Action<IReadOnlyList<JgsValue>, int, int> refuse)
    {
        if (!env.Builtins.TryGet(verb, out JgsValue existing) || existing.Type != JgsType.Function
            || existing.AsCallable is not BuiltinFunction inner)
        {
            return;
        }

        var guarded = new BuiltinFunction(verb, (args, line, col) =>
        {
            refuse(args, line, col);
            return inner.Call(args, line, col);
        })
        {
            MultiOutput = inner.MultiOutput is null
                ? null
                : (args, wanted, line, col) =>
                {
                    refuse(args, line, col);
                    return inner.CallMultiple(args, wanted, line, col);
                },
            BindsAnsAsStatement = inner.BindsAnsAsStatement,
            AutoCallsBare = inner.AutoCallsBare,
            KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
            TakesOutputCount = inner.TakesOutputCount,
            ScriptFile = inner.ScriptFile,
        };
        guarded.KeepsStringArguments = inner.KeepsStringArguments;
        env.Builtins.Register(verb, JgsValue.Function(guarded));
    }

    /// <summary>
    /// Puts R2025b's refusal of a group or a transform first round each verb that has one. Run after
    /// every verb is registered, so it wraps whatever each name ended up as.
    /// </summary>
    private static void RefuseGroupsAtAxesVerbs(JgsEnvironment env)
    {
        foreach ((string verb, GroupRefusal refusal) in AxesVerbGroupRefusals())
        {
            if (!env.Builtins.TryGet(verb, out JgsValue existing) || existing.Type != JgsType.Function
                || existing.AsCallable is not BuiltinFunction inner)
            {
                continue;
            }

            void Refuse(IReadOnlyList<JgsValue> args, int line, int col)
            {
                if (args.Count > 0 && GroupNamedBy(args[0]) is { } group)
                {
                    throw refusal(group, args.Count, line, col);
                }
            }

            var guarded = new BuiltinFunction(verb, (args, line, col) =>
            {
                Refuse(args, line, col);
                return inner.Call(args, line, col);
            })
            {
                MultiOutput = inner.MultiOutput is null
                    ? null
                    : (args, wanted, line, col) =>
                    {
                        Refuse(args, line, col);
                        return inner.CallMultiple(args, wanted, line, col);
                    },
                BindsAnsAsStatement = inner.BindsAnsAsStatement,
                AutoCallsBare = inner.AutoCallsBare,
                KnowsWhenDiscarded = inner.KnowsWhenDiscarded,
                TakesOutputCount = inner.TakesOutputCount,
                ScriptFile = inner.ScriptFile,
            };
            guarded.KeepsStringArguments = inner.KeepsStringArguments;
            env.Builtins.Register(verb, JgsValue.Function(guarded));
        }
    }
}
