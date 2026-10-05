using System.ComponentModel;

namespace JGraph.Core.Model;

/// <summary>The dialogs a <c>uifigure</c> lays over itself.</summary>
public enum UiOverlayKind
{
    /// <summary><c>uialert</c>: a message and an OK button.</summary>
    Alert,

    /// <summary><c>uiconfirm</c>: a message and a choice of buttons, which the script waits for.</summary>
    Confirm,

    /// <summary><c>uiprogressdlg</c>: a message and a bar.</summary>
    Progress,
}

/// <summary>
/// A dialog laid over a <c>uifigure</c> (app-building plan, U5): <c>uialert</c>, <c>uiconfirm</c> or
/// <c>uiprogressdlg</c>. It is no window of its own — the figure's window draws it over everything
/// else and, while one is modal, takes no input for what lies beneath. The figure holds its dialogs
/// oldest first, and the window shows the newest on top.
/// </summary>
public sealed class UiOverlayModel : GraphObject
{
    private string _title = string.Empty;
    private IReadOnlyList<string> _message = [];
    private string _icon = string.Empty;
    private UiImage? _image;
    private IReadOnlyList<string> _options = ["OK"];
    private int _defaultOption;
    private int _cancelOption;
    private bool _modal = true;
    private string _interpreter = "none";
    private double _value;
    private bool _indeterminate;
    private bool _showPercentage;
    private bool _cancelable;
    private string _cancelText = "Cancel";
    private bool _cancelRequested;

    public UiOverlayModel(UiOverlayKind kind)
    {
        Kind = kind;
        Name = kind.ToString();
    }

    [Browsable(false)]
    public UiOverlayKind Kind { get; }

    [Browsable(false)]
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>The message, a line to an element.</summary>
    [Browsable(false)]
    public IReadOnlyList<string> Message
    {
        get => _message;
        set => SetProperty(ref _message, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>One of MATLAB's stock icon words — error, warning, info, success, question — or empty for none.</summary>
    [Browsable(false)]
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value ?? string.Empty, InvalidationKind.Ui);
    }

    /// <summary>A picture given in place of a stock icon, or null.</summary>
    [Browsable(false)]
    public UiImage? Image
    {
        get => _image;
        set => SetProperty(ref _image, value, InvalidationKind.Ui);
    }

    /// <summary>The buttons, left to right: <c>OK</c> for an alert, a confirmation's options.</summary>
    [Browsable(false)]
    public IReadOnlyList<string> Options
    {
        get => _options;
        set => SetProperty(ref _options, value ?? [], InvalidationKind.Ui);
    }

    /// <summary>The button Return presses, from 0.</summary>
    [Browsable(false)]
    public int DefaultOption
    {
        get => _defaultOption;
        set => SetProperty(ref _defaultOption, value, InvalidationKind.Ui);
    }

    /// <summary>The button Escape and the close box press, from 0.</summary>
    [Browsable(false)]
    public int CancelOption
    {
        get => _cancelOption;
        set => SetProperty(ref _cancelOption, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Modal
    {
        get => _modal;
        set => SetProperty(ref _modal, value, InvalidationKind.Ui);
    }

    /// <summary>MATLAB's <c>Interpreter</c> word, kept; the text is drawn as written.</summary>
    [Browsable(false)]
    public string Interpreter
    {
        get => _interpreter;
        set => SetProperty(ref _interpreter, value ?? "none", InvalidationKind.Ui);
    }

    /// <summary>A progress dialog's fraction done, 0 to 1.</summary>
    [Browsable(false)]
    public double Value
    {
        get => _value;
        set => SetProperty(ref _value, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Indeterminate
    {
        get => _indeterminate;
        set => SetProperty(ref _indeterminate, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool ShowPercentage
    {
        get => _showPercentage;
        set => SetProperty(ref _showPercentage, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public bool Cancelable
    {
        get => _cancelable;
        set => SetProperty(ref _cancelable, value, InvalidationKind.Ui);
    }

    [Browsable(false)]
    public string CancelText
    {
        get => _cancelText;
        set => SetProperty(ref _cancelText, value ?? "Cancel", InvalidationKind.Ui);
    }

    /// <summary>Whether the person pressed a progress dialog's Cancel button.</summary>
    [Browsable(false)]
    public bool CancelRequested
    {
        get => _cancelRequested;
        set => SetProperty(ref _cancelRequested, value, InvalidationKind.Ui);
    }

    /// <summary>This dialog as a frame holds it.</summary>
    public UiOverlayFrame Snapshot() => new(
        this, Kind, _title, _message, _icon, _image, _options, _defaultOption, _cancelOption, _modal,
        _value, _indeterminate, _showPercentage, _cancelable, _cancelText);
}

/// <summary>One dialog over a figure, as a frame snapshot holds it.</summary>
public sealed record UiOverlayFrame(
    UiOverlayModel Source,
    UiOverlayKind Kind,
    string Title,
    IReadOnlyList<string> Message,
    string Icon,
    UiImage? Image,
    IReadOnlyList<string> Options,
    int DefaultOption,
    int CancelOption,
    bool Modal,
    double Value,
    bool Indeterminate,
    bool ShowPercentage,
    bool Cancelable,
    string CancelText);
