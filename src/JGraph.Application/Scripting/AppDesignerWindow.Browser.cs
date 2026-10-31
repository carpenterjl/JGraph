using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <summary>
/// The component browser in the window (App Designer window plan, stage W7a): its menu, a name typed into a
/// row, and its height over the inspector (<c>ComponentBrowserLayoutManager</c>).
/// </summary>
internal sealed partial class AppDesignerWindow
{
    private RowDefinition? _browserRow;
    private double _browserAsked = DesignBrowser.RowHeight;
    private double _browserPrevious = 65;
    private bool _browserManual;
    private double _browserManualTop;

    private void BuildBrowser(Grid side, RowDefinition row, GridSplitter splitter)
    {
        _browserRow = row;
        row.MinHeight = 58;
        _browser.IncludeLabelsSetting = Settings.GetBool(DesignSettings.IncludeComponentLabels);
        Settings.Changed += (_, name) =>
        {
            if (name == DesignSettings.IncludeComponentLabels)
            {
                _browser.IncludeLabelsSetting = Settings.GetBool(DesignSettings.IncludeComponentLabels);
            }
        };
        _browser.MenuProvider = (state, viewChanged, rename) =>
        {
            if (Active is not { } app || app.Canvas.Gestures is not { } gestures)
            {
                return [];
            }

            return gestures.BrowserMenu(state, app.InCodeView, viewChanged, rename, InsertAtCursor, app.CodeView.CaretIsEditable);
        };
        _browser.RenameEntered += (_, entered) => RenameComponent(entered.Component, entered.Name);
        _browser.HeightAsked += (_, asked) =>
        {
            _browserAsked = asked;
            FitBrowser(side);
        };
        side.SizeChanged += (_, _) => FitBrowser(side);

        // _continueSplitterDrag: small, the breadcrumbs; else the tree, at what the hand left.
        splitter.DragDelta += (_, _) =>
        {
            _browserManual = true;
            if (row.ActualHeight - 33 >= 65)
            {
                _browser.SetCollapsed(false);
                _browserManualTop = row.ActualHeight;
            }
            else
            {
                _browser.SetCollapsed(true);
                _browserManual = false;
            }
        };
    }

    private void FitBrowser(Grid side)
    {
        if (_browserRow is { } row && DesignBrowser.TopHeight(side.ActualHeight, _browserAsked, _browserManual, _browserManualTop, ref _browserPrevious) is { } top)
        {
            row.Height = new GridLength(top);
        }
    }

    /// <summary>
    /// A name typed into a row (<c>_handleObjectBrowserActionPerformed</c>): set when <c>isAppAttributeNameValid</c> lets
    /// it, and otherwise said in App Designer's alert, the row back to what it was.
    /// </summary>
    private void RenameComponent(DesignComponent component, string name)
    {
        if (Active is not { Document: var document } app)
        {
            return;
        }

        app.CodeView.Push();
        string? refused = document.WhyNotFunctionName(name, component.CodeName) ?? document.Rename(component, name);
        if (refused is not null)
        {
            ShowAlert("App Designer", refused);
        }
    }
}
