using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace JGraph.Controls;

/// <summary>
/// The text of a <c>uihyperlink</c> (app-building plan, U5): a text block that can be followed —
/// by a click, or through UI Automation's Invoke, which a plain text block does not offer and a
/// screen reader and a window check both need.
/// </summary>
internal sealed class UiLinkText : TextBlock
{
    /// <summary>Raised when the link is followed, however that was asked.</summary>
    public event EventHandler? Followed;

    public void Follow() => Followed?.Invoke(this, EventArgs.Empty);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Follow();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(UiLinkText owner) : TextBlockAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Hyperlink;

        protected override string GetClassNameCore() => nameof(UiLinkText);

        protected override bool IsControlElementCore() => true;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        public void Invoke()
        {
            if (!IsEnabled())
            {
                throw new System.Windows.Automation.ElementNotEnabledException();
            }

            // Out of the provider call before anything runs, as a button's own peer does.
            owner.Dispatcher.BeginInvoke(owner.Follow);
        }
    }
}
