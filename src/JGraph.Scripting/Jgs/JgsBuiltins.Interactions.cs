using JGraph.Api;
using JGraph.Core.Model;

namespace JGraph.Scripting.Jgs;

/// <summary>
/// M80: the gestures an axes answers to without a tool being chosen, as objects a script can make,
/// read and refuse.
/// <para>
/// Every one of these was already happening — dragging pans, the wheel zooms, a click pins a data
/// tip. What a script could not do was say which of them it wanted, which is why
/// <c>disableDefaultInteractivity</c> was accepted and did nothing since M71. It does something now,
/// and the three toggles beside it that name a legacy mode or a toolbar button still do not, because
/// this build has neither.
/// </para>
/// </summary>
internal static partial class JgsBuiltins
{
    /// <summary>The interaction constructors, and what each one makes.</summary>
    private static readonly (string Verb, Func<InteractionModel> Make)[] InteractionVerbs =
    [
        ("panInteraction", static () => new PanInteractionModel()),
        ("zoomInteraction", static () => new ZoomInteractionModel()),
        ("rulerPanInteraction", static () => new RulerPanInteractionModel()),
        ("regionZoomInteraction", static () => new RegionZoomInteractionModel()),
        ("rotateInteraction", static () => new RotateInteractionModel()),
        ("dataTipInteraction", static () => new DataTipInteractionModel()),
    ];

    private static void RegisterInteractionBuiltins(JgsEnvironment env)
    {
        foreach ((string verb, Func<InteractionModel> make) in InteractionVerbs)
        {
            string name = verb;
            Func<InteractionModel> build = make;

            // A bare name is the object, not the function: `ax.Interactions = [panInteraction
            // zoomInteraction]` is how MATLAB's own documentation writes it.
            env.Declare(name, JgsValue.Function(new BuiltinFunction(name, (args, line, col) =>
            {
                InteractionModel interaction = build();
                JgsHandleEntry entry = JgsHandleRegistry.EntryFor(interaction);
                foreach ((string option, JgsValue value) in Pairs(name, args, 0, line, col))
                {
                    JgsGraphicsProperties.Set(entry, option, value, line, col);
                }

                return JgsHandleRegistry.For(interaction);
            })
            { AutoCallsBare = true }));
        }
    }

    /// <summary>
    /// <c>disableDefaultInteractivity(ax)</c> and its opposite. The list itself is kept either way,
    /// so enabling gives back whatever a script had chosen rather than the defaults.
    /// </summary>
    private static JgsValue DefaultInteractivity(
        IReadOnlyList<JgsValue> args, int line, int col, bool on)
    {
        (AxesModel? named, IReadOnlyList<JgsValue> rest) = PeelAxes(args);
        ArityRange(on ? "enableDefaultInteractivity" : "disableDefaultInteractivity", rest, 0, 0, line, col);
        (named ?? JG.Gca()).InteractionsDisabled = !on;
        return JgsValue.Null;
    }
}
