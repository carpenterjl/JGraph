using System.Windows;
using JGraph.Application.Mvvm;
using JGraph.Runtime.Desktop.Theming;
using JGraph.Scripting.Designer;
using Microsoft.Win32;

namespace JGraph.Application;

/// <summary>
/// The Tools → Options dialog. A thin view over <see cref="OptionsViewModel"/>: it reflects the draft
/// into the controls on open and back on OK, then commits through the settings service. Cancel discards
/// everything untouched.
/// </summary>
public partial class OptionsWindow : Window
{
    private readonly OptionsViewModel _model;

    /// <summary>Creates the dialog over the given editable options draft.</summary>
    public OptionsWindow(OptionsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        InitializeComponent();

        OptionalLetBox.IsChecked = model.OptionalLet;
        ZeroBasedButton.IsChecked = !model.OneBasedIndexing;
        OneBasedButton.IsChecked = model.OneBasedIndexing;
        DirectoryBox.Text = model.DefaultScriptDirectory;

        AppThemeCombo.ItemsSource = model.AppThemes;
        AppThemeCombo.SelectedItem = model.SelectedAppTheme;
        LinkFigureThemeBox.IsChecked = model.LinkFigureThemeToAppTheme;

        LanguageCombo.ItemsSource = model.NewScriptLanguages;
        LanguageCombo.SelectedItem = model.DefaultNewScriptLanguage;
        ThemeCombo.ItemsSource = model.AvailableThemes;
        ThemeCombo.SelectedItem = model.DefaultTheme;

        CompilerCombo.ItemsSource = model.Compilers;
        CompilerCombo.SelectedItem = model.SelectedCompiler;

        PluginList.ItemsSource = model.Plugins;
        NoPluginsLabel.Visibility = model.Plugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // App Designer's settings (WO7): the grid interval from 10 to 50 by 5, the recent files 0 to 12.
        DesignSettings design = model.AppDesigner;
        DesignShowGridBox.IsChecked = design.GetBool(DesignSettings.ShowGrid);
        DesignGridIntervalCombo.ItemsSource = Enumerable.Range(0, 9).Select(static i => 10 + 5 * i).ToList();
        DesignGridIntervalCombo.SelectedItem = (int)design.GridIntervalValue;
        DesignSnapBox.IsChecked = design.GetBool(DesignSettings.SnapToGrid);
        DesignAlignmentHintsBox.IsChecked = design.GetBool(DesignSettings.ShowAlignmentHints);
        DesignResizingHintsBox.IsChecked = design.GetBool(DesignSettings.ShowResizingHints);
        DesignIncludeLabelsBox.IsChecked = design.GetBool(DesignSettings.IncludeComponentLabels);
        DesignRecentCombo.ItemsSource = Enumerable.Range(0, 13).ToList();
        DesignRecentCombo.SelectedItem = Math.Clamp(design.GetInt(DesignSettings.MaxFileListSize, 8), 0, 12);
        DesignAutoSaveBox.IsChecked = design.GetBool(DesignSettings.AutoSaveOnClickAway);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the default script folder",
            InitialDirectory = DirectoryBox.Text,
        };
        if (dialog.ShowDialog(this) == true)
        {
            DirectoryBox.Text = dialog.FolderName;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _model.OptionalLet = OptionalLetBox.IsChecked == true;
        _model.OneBasedIndexing = OneBasedButton.IsChecked == true;
        _model.DefaultScriptDirectory = DirectoryBox.Text;
        _model.DefaultNewScriptLanguage = LanguageCombo.SelectedItem as string ?? _model.DefaultNewScriptLanguage;
        _model.DefaultTheme = ThemeCombo.SelectedItem as string ?? _model.DefaultTheme;
        _model.SelectedAppTheme = AppThemeCombo.SelectedItem as AppThemeDescriptor ?? _model.SelectedAppTheme;
        _model.LinkFigureThemeToAppTheme = LinkFigureThemeBox.IsChecked == true;
        _model.SelectedCompiler = CompilerCombo.SelectedItem as JGraph.Scripting.LoadLibraryCompiler ?? _model.SelectedCompiler;
        DesignSettings design = _model.AppDesigner;
        design.Set(DesignSettings.ShowGrid, DesignShowGridBox.IsChecked == true);
        if (DesignGridIntervalCombo.SelectedItem is int interval)
        {
            design.Set(DesignSettings.GridInterval, interval);
        }

        design.Set(DesignSettings.SnapToGrid, DesignSnapBox.IsChecked == true);
        design.Set(DesignSettings.ShowAlignmentHints, DesignAlignmentHintsBox.IsChecked == true);
        design.Set(DesignSettings.ShowResizingHints, DesignResizingHintsBox.IsChecked == true);
        design.Set(DesignSettings.IncludeComponentLabels, DesignIncludeLabelsBox.IsChecked == true);
        if (DesignRecentCombo.SelectedItem is int recent)
        {
            design.Set(DesignSettings.MaxFileListSize, recent);
        }

        design.Set(DesignSettings.AutoSaveOnClickAway, DesignAutoSaveBox.IsChecked == true);
        _model.Apply();

        if (_model.PluginsChanged)
        {
            MessageBox.Show(
                this,
                "Your plugin changes will take effect the next time JGraph starts.",
                "Options",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        DialogResult = true;
    }
}
