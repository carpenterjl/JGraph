using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Deploy.Apps;
using Microsoft.Win32;

namespace JGraph.Application.Scripting;

/// <summary>
/// The shell's Apps menu (App Designer window plan W13): R2025b's Apps gallery as a menu over JGraph's add-ons folder
/// (<see cref="InstalledAddOns"/>). Each installed app and each toolbox's gallery entry is an item that runs the entry's
/// callback in the MATLAB session with its folders on the path, as the gallery's buttons do; Install App… takes an
/// <c>.mlappinstall</c> or <c>.mltbx</c> through <c>matlab.apputil.install</c> or <c>installToolbox</c>, Uninstall lists what
/// is installed, and Open Add-Ons Folder shows the folder. The menu reads the folder again each time it opens.
/// </summary>
public partial class ScriptWorkspaceWindow
{
    private void BuildAppsMenu()
    {
        AutomationProperties.SetAutomationId(AppsMenu, "Menu.Apps");
        AppsMenu.SubmenuOpened += (sender, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, AppsMenu))
            {
                FillAppsMenu();
            }
        };
        FillAppsMenu();
    }

    private void FillAppsMenu()
    {
        AppsMenu.Items.Clear();
        IReadOnlyList<GalleryEntry> entries;
        try
        {
            entries = InstalledAddOns.Default.Gallery();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read shows as empty; the menu says so in its first item.
            entries = [];
            AppsMenu.Items.Add(new MenuItem { Header = "The add-ons folder could not be read: " + ex.Message, IsEnabled = false });
        }

        if (entries.Count == 0)
        {
            AppsMenu.Items.Add(new MenuItem { Header = "No apps are installed", IsEnabled = false });
        }

        foreach (GalleryEntry entry in entries)
        {
            var item = new MenuItem { Header = entry.Label.Replace("_", "__", StringComparison.Ordinal), ToolTip = entry.Summary.Length > 0 ? entry.Summary : entry.Description.Length > 0 ? entry.Description : null };
            AutomationProperties.SetAutomationId(item, "Apps." + entry.Id);
            if (entry.Icon is { } icon && Picture(icon) is { } picture)
            {
                item.Icon = new Image { Source = picture, Width = 16, Height = 16, Stretch = Stretch.Uniform };
            }

            GalleryEntry captured = entry;
            item.Click += (_, _) => RunGalleryEntry(captured);
            AppsMenu.Items.Add(item);
        }

        AppsMenu.Items.Add(new Separator());
        var install = new MenuItem { Header = "_Install App…", ToolTip = "Install an app installation file (.mlappinstall) or a toolbox file (.mltbx)" };
        AutomationProperties.SetAutomationId(install, "Apps.Install");
        install.Click += (_, _) => InstallAppFromMenu();
        AppsMenu.Items.Add(install);
        var uninstall = new MenuItem { Header = "_Uninstall", IsEnabled = entries.Count > 0 };
        AutomationProperties.SetAutomationId(uninstall, "Apps.Uninstall");
        foreach (GalleryEntry entry in entries.GroupBy(static e => e.Id).Select(static g => g.First()))
        {
            var item = new MenuItem { Header = entry.Label.Replace("_", "__", StringComparison.Ordinal) };
            AutomationProperties.SetAutomationId(item, "Apps.Uninstall." + entry.Id);
            GalleryEntry captured = entry;
            item.Click += (_, _) => _ = RunWorkspaceStatementAsync("MATLAB", captured.IsApp
                ? $"matlab.apputil.uninstall('{Quote(captured.Id)}')"
                : $"matlab.addons.toolbox.uninstallToolbox(struct('Name', '', 'Version', '', 'Guid', '{Quote(captured.Id)}'))");
            uninstall.Items.Add(item);
        }

        AppsMenu.Items.Add(uninstall);
        var folder = new MenuItem { Header = "Open Add-_Ons Folder" };
        AutomationProperties.SetAutomationId(folder, "Apps.Folder");
        folder.Click += (_, _) =>
        {
            string root = InstalledAddOns.DefaultRoot;
            Directory.CreateDirectory(root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
        };
        AppsMenu.Items.Add(folder);
    }

    /// <summary>Runs a gallery entry as its button would: the entry's folders on the path, then its callback, in the MATLAB session.</summary>
    private void RunGalleryEntry(GalleryEntry entry)
    {
        string statement = entry.IsApp
            ? entry.Callback
            : (entry.PathFolders.Count > 0 ? "addpath(" + string.Join(", ", entry.PathFolders.Select(f => "'" + Quote(f) + "'")) + "); " : string.Empty) + entry.Callback + ";";
        _ = RunWorkspaceStatementAsync("MATLAB", statement);
    }

    private void InstallAppFromMenu()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Install App",
            Filter = "App and toolbox files (*.mlappinstall;*.mltbx)|*.mlappinstall;*.mltbx|App installation files (*.mlappinstall)|*.mlappinstall|Toolbox files (*.mltbx)|*.mltbx",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string file = dialog.FileName;
        string statement = file.EndsWith(".mltbx", StringComparison.OrdinalIgnoreCase)
            ? $"matlab.addons.toolbox.installToolbox('{Quote(file)}')"
            : $"matlab.apputil.install('{Quote(file)}')";
        _ = RunWorkspaceStatementAsync("MATLAB", statement);
    }

    private static string Quote(string text) => text.Replace("'", "''", StringComparison.Ordinal);

    private static BitmapImage? Picture(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UriFormatException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
