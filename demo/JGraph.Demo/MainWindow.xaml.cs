using System.Windows;
using System.Windows.Controls;
using JGraph.Core.Drawing;
using JGraph.Core.Model;
using JGraph.Plugins;

namespace JGraph.Demo;

/// <summary>The demo gallery: pick an example on the left, see it rendered on the right.</summary>
public partial class MainWindow : Window
{
    private readonly IReadOnlyList<GalleryExample> _examples;

    public MainWindow()
    {
        InitializeComponent();
        _examples = ExampleCatalog.Build();
        ExampleList.ItemsSource = _examples;
        ExampleList.SelectedIndex = 0;

        // Themes come from the plugin registry (built-in standard library: Light/Dark/Presentation/IEEE).
        ThemeSelector.ItemsSource = PluginRegistry.CreateDefault().Themes;
        ThemeSelector.SelectedIndex = 0;
    }

    private ITheme CurrentTheme => ThemeSelector.SelectedItem as ITheme ?? Theme.Light;

    private void OnExampleChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    private void ShowSelected()
    {
        if (Figure is null || ExampleList.SelectedItem is not GalleryExample example)
        {
            return;
        }

        ITheme theme = CurrentTheme;
        FigureModel figure = example.Build();
        theme.Apply(figure);

        Figure.Theme = theme;
        Figure.Figure = figure;
    }
}
