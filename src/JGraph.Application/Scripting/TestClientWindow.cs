using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using JGraph.Controls.Themes;
using JGraph.Deploy;

namespace JGraph.Application.Scripting;

/// <summary>
/// The Production Server Archive Compiler's Test Client (compiler and SDK plan, stage K19), as
/// R2025b's Test Client serves a task's functions on a local port and sends them requests: Start
/// runs a JGraph Production Server (<c>jgraph-mps</c>) over the built archive on the port given (0
/// for any free one), a request is a function, its inputs as a JSON array and the number of
/// outputs, sent as MATLAB Production Server's JSON (<c>{"nargout": n, "rhs": [...]}</c>), and the
/// reply and the server's lines are shown. Stop ends the server; closing the window does too.
/// </summary>
internal sealed class TestClientWindow : Window
{
    private readonly string _archive;
    private readonly TextBox _port = new() { Text = "9910", Width = 80 };
    private readonly Button _start = new() { Content = "Start", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0) };
    private readonly Button _stop = new() { Content = "Stop", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
    private readonly TextBlock _address = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _function = new() { IsEditable = true, Width = 200 };
    private readonly TextBox _inputs = new() { Text = "[]" };
    private readonly TextBox _nargout = new() { Text = "1", Width = 50 };
    private readonly Button _send = new() { Content = "Send", Padding = new Thickness(8, 2, 8, 2), IsEnabled = false };
    private readonly TextBox _reply = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), Height = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private LocalTestServer? _server;

    public TestClientWindow(string archive, IReadOnlyList<string> functions)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(functions);
        _archive = archive;
        Title = "Test Client";
        Width = 720;
        Height = 560;
        SetResourceReference(BackgroundProperty, ThemeKeys.Window);
        SetResourceReference(ForegroundProperty, ThemeKeys.Text);
        AutomationProperties.SetAutomationId(this, "TestClient");

        var panel = new DockPanel { Margin = new Thickness(10) };
        var serverRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        serverRow.Children.Add(new TextBlock { Text = "Port", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        AutomationProperties.SetAutomationId(_port, "Port");
        AutomationProperties.SetAutomationId(_start, "Start");
        AutomationProperties.SetAutomationId(_stop, "Stop");
        AutomationProperties.SetAutomationId(_address, "Address");
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => StopServer();
        serverRow.Children.Add(_port);
        serverRow.Children.Add(_start);
        serverRow.Children.Add(_stop);
        serverRow.Children.Add(_address);
        DockPanel.SetDock(serverRow, Dock.Top);
        panel.Children.Add(serverRow);

        var request = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        request.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (string name in functions)
        {
            _function.Items.Add(name);
        }

        if (_function.Items.Count > 0)
        {
            _function.SelectedIndex = 0;
        }

        AutomationProperties.SetAutomationId(_function, "Function");
        AutomationProperties.SetAutomationId(_inputs, "Inputs");
        AutomationProperties.SetAutomationId(_nargout, "Nargout");
        AutomationProperties.SetAutomationId(_send, "Send");
        _send.Click += async (_, _) => await SendAsync();
        var cells = new UIElement[]
        {
            new TextBlock { Text = "Function", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }, _function,
            new TextBlock { Text = "Inputs (JSON array)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) }, _inputs,
            new TextBlock { Text = "nargout", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) }, _nargout,
            _send,
        };
        for (int i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], i);
            request.Children.Add(cells[i]);
        }

        _send.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(request, Dock.Top);
        panel.Children.Add(request);

        var replyLabel = new TextBlock { Text = "Reply", FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(replyLabel, Dock.Top);
        panel.Children.Add(replyLabel);
        AutomationProperties.SetAutomationId(_reply, "Reply");
        _reply.Margin = new Thickness(0, 2, 0, 8);
        DockPanel.SetDock(_reply, Dock.Top);
        panel.Children.Add(_reply);
        var logLabel = new TextBlock { Text = "Server", FontWeight = FontWeights.SemiBold };
        DockPanel.SetDock(logLabel, Dock.Top);
        panel.Children.Add(logLabel);
        AutomationProperties.SetAutomationId(_log, "ServerLog");
        _log.Margin = new Thickness(0, 2, 0, 0);
        panel.Children.Add(_log);
        Content = panel;
        Closed += (_, _) =>
        {
            StopServer();
            _http.Dispose();
        };
    }

    /// <summary>The running server, or null.</summary>
    public LocalTestServer? Server => _server;

    private async Task StartAsync()
    {
        if (_server is not null)
        {
            return;
        }

        if (!int.TryParse(_port.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int port))
        {
            Log("The port must be a number (0 for any free port).");
            return;
        }

        _start.IsEnabled = false;
        Log($"Starting jgraph-mps over {_archive} ...");
        try
        {
            LocalTestServer server = await Task.Run(() => LocalTestServer.Start(_archive, port, line => Dispatcher.BeginInvoke(() => Log(line))));
            _server = server;
            _address.Text = server.Url + "/" + server.ArchiveName + "/<function>";
            _stop.IsEnabled = true;
            _send.IsEnabled = true;
            Log($"Listening at {server.Url}; the archive is {server.ArchiveName}.");
        }
        catch (InvalidOperationException ex)
        {
            Log(ex.Message);
            _start.IsEnabled = true;
        }
    }

    private void StopServer()
    {
        if (_server is not { } server)
        {
            return;
        }

        server.Dispose();
        _server = null;
        _address.Text = "";
        _stop.IsEnabled = false;
        _send.IsEnabled = false;
        _start.IsEnabled = true;
        Log("Stopped.");
    }

    private async Task SendAsync()
    {
        if (_server is not { } server)
        {
            return;
        }

        string function = (_function.Text ?? "").Trim();
        if (function.Length == 0)
        {
            Log("Name a function to call.");
            return;
        }

        string inputs = _inputs.Text.Trim().Length == 0 ? "[]" : _inputs.Text.Trim();
        string nargout = int.TryParse(_nargout.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n.ToString(CultureInfo.InvariantCulture) : "1";
        string body = $"{{\"nargout\": {nargout}, \"rhs\": {inputs}}}";
        string url = $"{server.Url}/{server.ArchiveName}/{function}";
        _send.IsEnabled = false;
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync(new Uri(url), content);
            string reply = await response.Content.ReadAsStringAsync();
            _reply.Text = $"POST {url}\n{body}\n\nHTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{reply}".Replace("\n", Environment.NewLine, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            _reply.Text = $"POST {url}{Environment.NewLine}{ex.Message}";
        }
        finally
        {
            _send.IsEnabled = _server is not null;
        }
    }

    private void Log(string line)
    {
        _log.AppendText(line + Environment.NewLine);
        _log.ScrollToEnd();
    }
}
