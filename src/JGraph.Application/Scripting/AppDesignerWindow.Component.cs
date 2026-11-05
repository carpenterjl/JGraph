using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JGraph.Controls.Designer;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <content>
/// A custom UI component in the designer's window (App Designer window plan W15, chip 162): the toolstrip's
/// swap for one (the Callback split button and Event in place of Callback and App Input Arguments, Configure for
/// Apps in place of SHARE, no Convert), the Property buttons opening the Add Property dialog, the Event–Callback
/// Pairs tab, Configure for Apps saving the component and asking the MATLAB session for
/// <c>appdesigner.customcomponent.configureMetadata</c>, and the dialogs the code browser and the tab ask for.
/// </content>
internal sealed partial class AppDesignerWindow
{
    /// <summary>Adds an event with its public callback (EDITOR > Callback > Event–Public Callback Pair, Event).</summary>
    public static readonly RoutedCommand AddEventPair = new(nameof(AddEventPair), typeof(AppDesignerWindow));

    /// <summary>Configure for Apps: the component's library metadata (DESIGNER > Configure for Apps).</summary>
    public static readonly RoutedCommand ConfigureForApps = new(nameof(ConfigureForApps), typeof(AppDesignerWindow));

    private const string AppDetailsButtonTag = DesignToolstrip.Prefix + "designerTab.share.appDetailsButton";
    private const string ShareAllOptionsTag = DesignToolstrip.Prefix + "designerTab.share.shareAllOptions";
    private const string InputArgumentsButtonTag = DesignToolstrip.Prefix + "editorTab.edit.addInputArgumentsButton";
    private const string ResponsifyButtonTag = DesignToolstrip.Prefix + "layoutTab.file.responsifyPushButton";

    private readonly DesignEventPairsView _eventPairs = new();
    private TabItem? _eventPairsTab;

    /// <summary>Whether the active document is a custom UI component.</summary>
    private bool ActiveIsComponent => Active?.Document.IsComponent == true;

    /// <summary>
    /// A component was registered or its registry changed (the metadata dialog): the open apps read their libraries again, so
    /// that the Component Library lists it (R2025b's registration service re-reads the cache).
    /// </summary>
    public void RefreshUserComponents()
    {
        foreach (AppTab app in Apps)
        {
            app.Document.RefreshUserComponents(PathFolders?.Invoke());
        }

        _palette.SetUserEntries(Active?.Document.UserPaletteEntries ?? []);
    }

    private void BuildComponentPanel()
    {
        _eventPairsTab = new TabItem { Header = DesignEventPairsView.Title, Content = _eventPairs, Visibility = Visibility.Collapsed };
        System.Windows.Automation.AutomationProperties.SetAutomationId(_eventPairsTab, "PropertiesPanel.EventPairs");
        _right.Items.Add(_eventPairsTab);
        _eventPairs.AddRequested += (_, _) => ShowAddEvent();
        _eventPairs.EditRequested += (_, e) => ShowEditEvent(e);
        _eventPairs.Problem += (_, problem) => ShowAlert("App Designer", problem);
        _eventPairs.Copied += (_, line) => _status.Text = $"Copied: {line}";
        _codeBrowser.AddEventRequested += (_, _) => ShowAddEvent();
        _codeBrowser.EditEventRequested += (_, e) => ShowEditEvent(e);
        _codeBrowser.AddManagedPropertyRequested += (_, access) => ShowAddManagedProperty(access);
        _codeBrowser.EditManagedPropertyRequested += (_, p) => ShowEditManagedProperty(p);
        CommandBindings.Add(new CommandBinding(AddEventPair, (_, _) => ShowAddEvent(), (_, e) => e.CanExecute = Active is { Document: { Code: not null, IsComponent: true } } && !_debugging));
        CommandBindings.Add(new CommandBinding(ConfigureForApps, (_, _) => RunConfigureForApps(), (_, e) => e.CanExecute = ActiveIsComponent && !_running && !_debugging));
    }

    /// <summary>The Event–Callback Pairs tab stands only while a component's root is selected (the inspector's first tab says the component).</summary>
    private void ShowComponentPanel()
    {
        if (_eventPairsTab is null)
        {
            return;
        }

        bool shown = Active?.Document is { IsComponent: true, AppSelected: true };
        _eventPairs.Document = shown ? Active?.Document : null;
        _eventPairsTab.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (!shown && ReferenceEquals(_right.SelectedItem, _eventPairsTab))
        {
            _right.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// The toolstrip as App Designer shows it for the active document's kind (<c>AppDesignerToolstrip.updateForDocumentType</c>): a
    /// component's code has the Callback split button, Event and Configure for Apps and no App Details, SHARE, App Input Arguments or Convert.
    /// </summary>
    private void ShowComponentToolstrip(AppTab? app)
    {
        bool component = app?.Document.IsComponent == true;
        _toolstrip.SetVisible(DesignToolstrip.AddCallbackButton, !component);
        _toolstrip.SetVisible(InputArgumentsButtonTag, !component);
        _toolstrip.SetVisible(ResponsifyButtonTag, !component);
        _toolstrip.SetVisible(AppDetailsButtonTag, !component);
        _toolstrip.SetVisible(ShareAllOptionsTag, !component);
        _toolstrip.SetVisible(DesignToolstrip.AddCallbackSplitButton, component);
        _toolstrip.SetVisible(DesignToolstrip.AddEventButton, component);
        _toolstrip.SetVisible(DesignToolstrip.ConfigureMetadataColumn, component);
        bool code = app is { Document.Code: not null } && !_debugging;
        _toolstrip.SetEnabled(DesignToolstrip.AddCallbackSplitButton, code);
        _toolstrip.SetEnabled(DesignToolstrip.AddEventButton, code);
        _toolstrip.SetEnabled("AD_EDITOR_ADD_CALLBACK_EVENT_PAIR", code);
        _toolstrip.SetEnabled("AD_CONFIGURE_METADATA", component && !_running && !_debugging);
    }

    private void ShowAddEvent()
    {
        if (Active is not { Document: { Code: not null, IsComponent: true } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.AddEvent(this, document, null) is { } name)
        {
            _eventPairs.Select(name);
            _status.Text = $"Added the event {name} with its public callback {name}Fcn.";
        }
    }

    private void ShowEditEvent(DesignCodeEvent existing)
    {
        if (Active is not { Document: { Code: not null, IsComponent: true } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.AddEvent(this, document, existing) is { } name)
        {
            _eventPairs.Select(name);
        }
    }

    /// <summary>The Property buttons: a component's open the Add Property dialog (<c>AddManagedUserPropertyAction</c>); an app's insert a property block.</summary>
    private void AddPropertyOrManaged(string access)
    {
        if (ActiveIsComponent)
        {
            ShowAddManagedProperty(access);
        }
        else
        {
            Declared(Active?.Document.InsertUserProperty(access), function: false);
        }
    }

    private void ShowAddManagedProperty(string access)
    {
        if (Active is not { Document: { Code: not null, IsComponent: true } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.AddManagedProperty(this, document, null, access) is { } name)
        {
            SetView(app, code: true);
            app.CodeView.SelectDeclaration(name, function: false);
        }
    }

    private void ShowEditManagedProperty(DesignManagedProperty existing)
    {
        if (Active is not { Document: { Code: not null, IsComponent: true } document } app)
        {
            return;
        }

        app.CodeView.Push();
        if (AppDesignerDialogs.AddManagedProperty(this, document, existing, existing.Access) is { } name)
        {
            SetView(app, code: true);
            app.CodeView.SelectDeclaration(name, function: false);
        }
    }

    /// <summary>
    /// Configure for Apps (<c>ConfigureMetadataAction</c>): the component saved first, then the MATLAB session asked for
    /// <c>appdesigner.customcomponent.configureMetadata</c> on its file, which brings the dialog up through the session's door.
    /// </summary>
    private void RunConfigureForApps()
    {
        if (Active is not { Document.IsComponent: true } app)
        {
            return;
        }

        if (SessionStatement is not { } run)
        {
            _status.Text = "There is no MATLAB session to configure the component in.";
            return;
        }

        if (!SaveBeforeRun(app) || app.Document.Path is not { } path)
        {
            return;
        }

        _status.Text = $"Configuring {Path.GetFileName(path)} for apps…";
        _ = run($"appdesigner.customcomponent.configureMetadata('{path.Replace("'", "''", StringComparison.Ordinal)}')");
    }
}
