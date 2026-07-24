using System.Windows;
using JGraph.Application.Mvvm;
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

        LanguageCombo.ItemsSource = model.NewScriptLanguages;
        LanguageCombo.SelectedItem = model.DefaultNewScriptLanguage;
        ThemeCombo.ItemsSource = model.AvailableThemes;
        ThemeCombo.SelectedItem = model.DefaultTheme;

        PluginList.ItemsSource = model.Plugins;
        NoPluginsLabel.Visibility = model.Plugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
