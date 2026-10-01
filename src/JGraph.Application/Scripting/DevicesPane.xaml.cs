using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using JGraph.Devices;
using JGraph.Devices.Usb;
using JGraph.Scripting.Devices;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Devices pane (device classes plan, stage 14, ADR 0197): the serial ports and the USB tree of
/// <see cref="DevicePaneModel"/>, kept current by the hot-plug notifications <c>jgraph.usb.watch</c>
/// uses. A node's actions write a line of code at the console prompt or point the Serial Explorer at
/// a port; the pane itself opens no device.
/// </summary>
/// <remarks>
/// Nothing is read until the pane is first shown, and nothing is read while it is hidden: reading the
/// USB tree asks every device for its descriptors, which is not something to do behind a closed tab.
/// </remarks>
public partial class DevicesPane : UserControl, IDisposable
{
    /// <summary>COM port device interfaces: what a Bluetooth or motherboard port arrives as, which the USB class does not cover.</summary>
    private static readonly Guid ComPortInterface = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    private readonly DispatcherTimer _settle;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private UsbWatcher? _usbWatch;
    private UsbWatcher? _portWatch;
    private bool _reading;
    private bool _stale = true;
    private bool _everRead;
    private bool _disposed;

    /// <summary>Creates the pane; it reads the machine when it is first shown.</summary>
    public DevicesPane()
    {
        InitializeComponent();

        // A device arriving raises several notifications (one per interface) and its COM port appears
        // a moment after it; one read once they have settled shows the whole of it.
        _settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            RefreshIfVisible();
        };
        IsVisibleChanged += (_, _) => RefreshIfVisible();
    }

    /// <summary>Raised with a line of code to write at the console prompt.</summary>
    public event Action<string>? CodeRequested;

    /// <summary>Raised with a serial port the Serial Explorer should select.</summary>
    public event Action<string>? SerialExplorerRequested;

    /// <summary>Raised with a sentence for the window's status bar.</summary>
    public event Action<string>? StatusChanged;

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _settle.Stop();
        _usbWatch?.Dispose();
        _portWatch?.Dispose();
        _usbWatch = null;
        _portWatch = null;
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        _stale = true;
        RefreshIfVisible();
    }

    /// <summary>Reads the machine again when the pane is showing and something may have changed.</summary>
    private void RefreshIfVisible()
    {
        if (_disposed || !IsVisible || !_stale || _reading)
        {
            return;
        }

        _stale = false;
        _reading = true;
        StateText.Text = _everRead ? "Reading…" : "Reading the devices…";
        StartWatching();
        _ = ReadAsync();
    }

    private async Task ReadAsync()
    {
        IReadOnlyList<DeviceNode>? nodes = null;
        string? failure = null;
        try
        {
            // Off the UI thread: the hub requests behind the USB tree take tens to hundreds of milliseconds.
            nodes = await Task.Run(static () => DevicePaneModel.Build(DevicePaneModel.Read())).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Whatever the enumeration threw, the pane says so and stays usable: a fault left to
            // escape here would leave it reading for ever.
            failure = ex.Message;
        }

        _reading = false;
        if (_disposed)
        {
            return;
        }

        if (nodes is not null)
        {
            _everRead = true;
            Show(nodes);
            StateText.Text = $"Read at {DateTime.Now:HH:mm:ss}";
        }
        else
        {
            StateText.Text = "Could not read the devices: " + failure;
        }

        // A device that came or went while this read ran marked the tree stale again: read once more.
        RefreshIfVisible();
    }

    /// <summary>Registers for arrivals and removals once; a machine that refuses leaves the Refresh button.</summary>
    private void StartWatching()
    {
        if (_usbWatch is not null)
        {
            return;
        }

        try
        {
            _usbWatch = new UsbWatcher();
            _usbWatch.Changed += OnDevicesChanged;
            _portWatch = new UsbWatcher(ComPortInterface);
            _portWatch.Changed += OnDevicesChanged;
        }
        catch (DeviceOpenException ex)
        {
            StatusChanged?.Invoke("The Devices pane cannot watch for devices arriving (" + ex.Message + "); use its refresh button.");
        }
    }

    /// <summary>Runs on the configuration manager's thread: marks the tree stale and lets the notifications settle.</summary>
    private void OnDevicesChanged(string instanceId, string path, bool arrived)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            _stale = true;
            _settle.Stop();
            _settle.Start();
        });
    }

    // --- the tree ------------------------------------------------------------------------------------

    private void Show(IReadOnlyList<DeviceNode> nodes)
    {
        // What the user collapsed stays collapsed across a read; everything else opens, since the
        // tree is small and a device hidden under a closed hub looks like a device not found.
        RememberCollapsed(Tree.Items, "");
        Tree.Items.Clear();
        foreach (DeviceNode node in nodes)
        {
            Tree.Items.Add(Item(node, ""));
        }
    }

    private void RememberCollapsed(ItemCollection items, string path)
    {
        foreach (TreeViewItem item in items.OfType<TreeViewItem>())
        {
            string key = path + "/" + ((DeviceNode)item.Tag).Title;
            if (item.HasItems && !item.IsExpanded)
            {
                _collapsed.Add(key);
            }
            else
            {
                _collapsed.Remove(key);
            }

            RememberCollapsed(item.Items, key);
        }
    }

    private TreeViewItem Item(DeviceNode node, string path)
    {
        string key = path + "/" + node.Title;
        var header = new TextBlock();
        header.Inlines.Add(new Run(node.Title)
        {
            FontWeight = node.Kind == DeviceNodeKind.Group ? FontWeights.SemiBold : FontWeights.Normal,
        });
        if (node.Detail.Length > 0)
        {
            var detail = new Run("   " + node.Detail);
            detail.SetResourceReference(TextElement.ForegroundProperty, "JG.Brush.TextSecondary");
            header.Inlines.Add(detail);
        }

        var item = new TreeViewItem
        {
            Header = header,
            Tag = node,
            IsExpanded = !_collapsed.Contains(key),
            ToolTip = node.ToolTip.Length > 0 ? node.ToolTip : null,
        };
        if (node.Actions.Count > 0)
        {
            var menu = new ContextMenu();
            foreach (DeviceAction action in node.Actions)
            {
                var entry = new MenuItem { Header = action.Title.Replace("_", "__", StringComparison.Ordinal) };
                if (action.Kind == DeviceActionKind.Code)
                {
                    entry.ToolTip = action.Text;
                }

                entry.Click += (_, _) => Run(action);
                menu.Items.Add(entry);
            }

            item.ContextMenu = menu;
        }

        foreach (DeviceNode child in node.Children)
        {
            item.Items.Add(Item(child, key));
        }

        return item;
    }

    private void Run(DeviceAction action)
    {
        switch (action.Kind)
        {
            case DeviceActionKind.Code:
                CodeRequested?.Invoke(action.Text);
                break;
            case DeviceActionKind.SerialExplorer:
                SerialExplorerRequested?.Invoke(action.Text);
                break;
            case DeviceActionKind.Copy:
                try
                {
                    Clipboard.SetText(action.Text);
                    StatusChanged?.Invoke("Copied " + action.Text);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    StatusChanged?.Invoke("The clipboard is busy; try again.");
                }

                break;
        }
    }

    /// <summary>A double-click on a leaf does its first action; on a hub it only opens or closes it.</summary>
    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is { Tag: DeviceNode { Children.Count: 0, Actions.Count: > 0 } node })
        {
            Run(node.Actions[0]);
            e.Handled = true;
        }
    }

    /// <summary>A right-click selects the row under it, so the menu that opens is that row's.</summary>
    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is { } item)
        {
            item.IsSelected = true;
        }
    }

    private static TreeViewItem? ItemAt(object source)
    {
        DependencyObject? at = source as DependencyObject;
        while (at is not null and not TreeViewItem)
        {
            at = at is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(at)
                : LogicalTreeHelper.GetParent(at);
        }

        return at as TreeViewItem;
    }
}
