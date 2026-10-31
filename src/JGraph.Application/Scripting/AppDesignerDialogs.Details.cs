using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JGraph.Controls.Themes;
using JGraph.Scripting.Designer;

namespace JGraph.Application.Scripting;

/// <content>App Details and the Help Text dialog (App Designer window plan W8a).</content>
internal static partial class AppDesignerDialogs
{
    /// <summary>
    /// App Designer's App Details (<c>AppDetailsDialogController</c>, with <c>AppDetailsDialogContent</c> for an <c>.mlapp</c> and
    /// <c>PlainTextAppDetailsDialogContent</c> for a plain-text app): an accordion of Sharing Details and Code Options, the second
    /// open when the input arguments asked for it. OK makes whatever changed one step (<see cref="DesignDocument.SetAppDetails"/>).
    /// </summary>
    /// <returns>Whether the input arguments changed, after which App Designer goes to the startup function.</returns>
    public static bool AppDetails(Window owner, DesignDocument document, bool codeOptions)
    {
        DesignAppDetailsValues held = document.AppDetailsValues;
        bool plain = document.DetailsAsPlainText;
        string shownVersion = held.Version;
        DesignScreenshotMode mode = held.Mode;
        string thumbnail = held.Thumbnail;

        var dialog = Dialog(owner, DesignAppDetails.Title);
        AutomationProperties.SetAutomationId(dialog, "AppDetails");
        dialog.Width = 500;

        TextBox Field(string id, string name, string text, int limit)
        {
            var box = new TextBox { Text = text, MaxLength = limit };
            AutomationProperties.SetAutomationId(box, id);
            AutomationProperties.SetName(box, name);
            return box;
        }

        TextBox Area(string id, string name, string text, int limit, double height)
        {
            TextBox box = Field(id, name, text, limit);
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.Height = height;
            return box;
        }

        static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 2) };

        TextBox nameBox = Field("AppDetails.Name", "Name", held.Name, DesignAppDetails.NameLimit);
        TextBox versionBox = Field("AppDetails.Version", "Version", held.Version, DesignAppDetails.NameLimit);
        TextBox authorBox = Field("AppDetails.Author", "Author", held.Author, plain ? DesignAppDetails.NameLimit : DesignAppDetails.MlappAuthorLimit);
        TextBox summaryBox = Area("AppDetails.Summary", "Summary", held.Summary, DesignAppDetails.SummaryLimit, plain ? 35 : 93);
        TextBox descriptionBox = Area("AppDetails.Description", "Description", held.Description, DesignAppDetails.DescriptionLimit, 96);
        var versionError = new TextBlock { TextWrapping = TextWrapping.Wrap };
        versionError.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        AutomationProperties.SetAutomationId(versionError, "AppDetails.VersionError");

        // The thumbnail: 220 by 165, its words until it holds an image, the button over it.
        string words = DesignAppDetails.AutoCaptureWords;
        var picture = new Image { Stretch = Stretch.Uniform, MaxWidth = DesignAppDetails.ThumbnailWidth, MaxHeight = DesignAppDetails.ThumbnailHeight };
        AutomationProperties.SetAutomationId(picture, "AppDetails.Thumbnail");
        var pictureWords = new TextBlock { Text = words, FontSize = 12, Padding = new Thickness(15), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(pictureWords, "AppDetails.ThumbnailWords");
        var select = new Button { Content = DesignAppDetails.SelectImage, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8, 2, 8, 2) };
        AutomationProperties.SetAutomationId(select, "AppDetails.SelectImage");
        var box220 = new Grid { Width = DesignAppDetails.ThumbnailWidth, Height = DesignAppDetails.ThumbnailHeight, ToolTip = words };
        box220.Children.Add(pictureWords);
        box220.Children.Add(picture);
        box220.Children.Add(select);
        var frame = new Border { BorderThickness = new Thickness(1), Child = box220, HorizontalAlignment = HorizontalAlignment.Left };
        frame.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.Border);

        void ShowPicture(string uri)
        {
            picture.Source = ImageOf(uri);
            pictureWords.Text = uri.Length > 0 && picture.Source is null ? DesignAppDetails.ImageLoadError : words;
            pictureWords.Visibility = picture.Source is null ? Visibility.Visible : Visibility.Collapsed;
        }

        RadioButton? auto = null;
        RadioButton? custom = null;
        RadioButton? none = null;
        void ChooseImage()
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Title = DesignAppDetails.ImageDialogTitle, Filter = DesignAppDetails.ImageFilter };
            if (picker.ShowDialog(dialog) != true)
            {
                return;
            }

            if (DesignAppDetails.WhyNotImage(picker.FileName) is { } why)
            {
                ShowInvalid(dialog, why);
                return;
            }

            try
            {
                string uri = plain ? DesignAppDetails.UriForPlainText(picker.FileName) : DesignAppDetails.UriForPackage(picker.FileName);
                if (ImageOf(uri) is null)
                {
                    ShowInvalid(dialog, DesignAppDetails.InvalidImage);
                    return;
                }

                thumbnail = uri;
                mode = DesignScreenshotMode.Manual;
                ShowPicture(uri);
                if (custom is not null)
                {
                    custom.IsChecked = true;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ShowInvalid(dialog, DesignAppDetails.InvalidImage);
            }
        }

        select.Click += (_, _) => ChooseImage();
        ShowPicture(mode == DesignScreenshotMode.None ? string.Empty : thumbnail);

        var sharing = new StackPanel { Margin = new Thickness(8) };
        sharing.Children.Add(new TextBlock { Text = DesignAppDetails.HelpWords, TextWrapping = TextWrapping.Wrap, FontSize = 13, Padding = new Thickness(0, 0, 0, plain ? 5 : 15) });
        if (plain)
        {
            // The grid of six columns: name 2, version 1, author 3; the thumbnail 3 beside its source.
            var heads = new Grid();
            heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heads.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            heads.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heads.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void Column(int column, string title, TextBox field)
            {
                TextBlock head = Heading(title);
                Grid.SetColumn(head, column);
                field.Margin = new Thickness(0, 0, column < 2 ? 10 : 0, 0);
                Grid.SetColumn(field, column);
                Grid.SetRow(field, 1);
                heads.Children.Add(head);
                heads.Children.Add(field);
            }

            Column(0, "Name", nameBox);
            Column(1, "Version", versionBox);
            Column(2, "Author", authorBox);
            sharing.Children.Add(heads);
            sharing.Children.Add(versionError);
            sharing.Children.Add(Heading("Summary"));
            sharing.Children.Add(summaryBox);
            sharing.Children.Add(Heading("Description"));
            sharing.Children.Add(descriptionBox);
            sharing.Children.Add(Heading("Thumbnail"));
            var below = new Grid();
            below.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            below.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            below.Children.Add(frame);
            var source = new StackPanel { Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(source, 1);
            source.Children.Add(new TextBlock { Text = "Source:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            RadioButton Radio(string text, string tip, string id, DesignScreenshotMode value)
            {
                var radio = new RadioButton { Content = text, ToolTip = tip, IsChecked = mode == value, GroupName = "AppDetailsSource", Margin = new Thickness(0, 2, 0, 2), VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetAutomationId(radio, id);
                // _handleSource*Changed: the image shown goes, whichever way it is changed.
                radio.Checked += (_, _) =>
                {
                    if (mode != value)
                    {
                        mode = value;
                        ShowPicture(value == DesignScreenshotMode.Manual ? thumbnail : string.Empty);
                    }
                };
                return radio;
            }

            auto = Radio("Auto", "Automatically capture app thumbnail when the app is run from App Designer", "AppDetails.Source.Auto", DesignScreenshotMode.Auto);
            custom = Radio("Custom", "Select custom image to use as app thumbnail", "AppDetails.Source.Custom", DesignScreenshotMode.Manual);
            none = Radio("None", "Do not include an app thumbnail", "AppDetails.Source.None", DesignScreenshotMode.None);
            var browse = new Button { Content = "...", Width = 24, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Select custom image to use as app thumbnail" };
            AutomationProperties.SetAutomationId(browse, "AppDetails.Source.Browse");
            browse.Click += (_, _) => ChooseImage();
            var customRow = new StackPanel { Orientation = Orientation.Horizontal };
            customRow.Children.Add(custom);
            customRow.Children.Add(browse);
            source.Children.Add(auto);
            source.Children.Add(customRow);
            source.Children.Add(none);
            below.Children.Add(source);
            sharing.Children.Add(below);
        }
        else
        {
            // AppDetailsDialogContentTemplate: name and version on a row; author and summary beside the picture; description under them.
            var row = new DockPanel();
            versionBox.Width = 70;
            versionBox.Margin = new Thickness(5, 0, 0, 0);
            DockPanel.SetDock(versionBox, Dock.Right);
            row.Children.Add(versionBox);
            row.Children.Add(nameBox);
            var titles = new DockPanel();
            var versionHead = Heading("Version");
            versionHead.Width = 70;
            versionHead.Margin = new Thickness(5, 0, 0, 2);
            DockPanel.SetDock(versionHead, Dock.Right);
            titles.Children.Add(versionHead);
            titles.Children.Add(Heading("Name"));
            sharing.Children.Add(titles);
            sharing.Children.Add(row);
            sharing.Children.Add(versionError);
            var middle = new Grid { Margin = new Thickness(0, 5, 0, 0) };
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            left.Children.Add(Heading("Author"));
            left.Children.Add(authorBox);
            left.Children.Add(Heading("Summary"));
            left.Children.Add(summaryBox);
            middle.Children.Add(left);
            frame.Margin = new Thickness(0, 21, 0, 0);
            Grid.SetColumn(frame, 1);
            middle.Children.Add(frame);
            sharing.Children.Add(middle);
            sharing.Children.Add(Heading("Description"));
            sharing.Children.Add(descriptionBox);
        }

        // Code Options (AppDetailsCodeOptionsContent).
        var code = new Grid { Margin = new Thickness(8) };
        code.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        code.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        code.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        code.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        code.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var single = new CheckBox { IsChecked = document.SingleInstance, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "App allows only one running instance at a time." };
        AutomationProperties.SetAutomationId(single, "CodeOptions.SingleInstance");
        AutomationProperties.SetName(single, "Single Running Instance");
        var singleWords = new TextBlock { Text = "Single Running Instance", VerticalAlignment = VerticalAlignment.Center };
        code.Children.Add(singleWords);
        Grid.SetColumn(single, 1);
        code.Children.Add(single);
        var argumentsWords = new TextBlock { Text = "Input Arguments", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        Grid.SetRow(argumentsWords, 1);
        code.Children.Add(argumentsWords);
        var arguments = new TextBox { Text = document.InputParameters, Margin = new Thickness(12, 10, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(arguments, "CodeOptions.InputArguments");
        AutomationProperties.SetName(arguments, "Input Arguments");
        var argumentsHolder = new Grid();
        var empty = new TextBlock { Text = "arg1, arg2, ...", Margin = new Thickness(16, 10, 0, 10), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Opacity = 0.6 };
        argumentsHolder.Children.Add(arguments);
        argumentsHolder.Children.Add(empty);
        Grid.SetRow(argumentsHolder, 1);
        Grid.SetColumn(argumentsHolder, 1);
        code.Children.Add(argumentsHolder);
        var argumentsError = new TextBlock { TextWrapping = TextWrapping.Wrap };
        argumentsError.SetResourceReference(TextBlock.ForegroundProperty, ThemeKeys.Error);
        AutomationProperties.SetAutomationId(argumentsError, "AppDetails.ArgumentsError");
        Grid.SetRow(argumentsError, 2);
        Grid.SetColumnSpan(argumentsError, 2);
        code.Children.Add(argumentsError);

        // The accordion: one section open at a time.
        var sharingSection = new Expander { Header = DesignAppDetails.SharingDetailsTitle, Content = sharing, IsExpanded = !codeOptions };
        var codeSection = new Expander { Header = DesignAppDetails.CodeOptionsTitle, Content = code, IsExpanded = codeOptions };
        AutomationProperties.SetAutomationId(sharingSection, "AppDetails.SharingDetails");
        AutomationProperties.SetName(sharingSection, DesignAppDetails.SharingDetailsTitle);
        AutomationProperties.SetAutomationId(codeSection, "AppDetails.CodeOptions");
        AutomationProperties.SetName(codeSection, DesignAppDetails.CodeOptionsTitle);
        sharingSection.Expanded += (_, _) => codeSection.IsExpanded = false;
        codeSection.Expanded += (_, _) => sharingSection.IsExpanded = false;
        sharingSection.Collapsed += (_, _) => codeSection.IsExpanded = true;
        codeSection.Collapsed += (_, _) => sharingSection.IsExpanded = true;
        var body = new Grid();
        var sections = new StackPanel();
        sections.Children.Add(sharingSection);
        sections.Children.Add(codeSection);
        body.Children.Add(sections);
        Button ok = Buttons(dialog, body, "OK");
        AutomationProperties.SetAutomationId(ok, "AppDetails.OK");

        // _toggleValidationOkButtonState over the two states; the version's typing state asks less than its change's.
        bool versionBad = false;
        bool argumentsBad = false;
        void Refresh() => ok.IsEnabled = !versionBad && !argumentsBad;
        versionBox.TextChanged += (_, _) =>
        {
            versionBad = !DesignAppDetails.MayBecomeVersion(versionBox.Text) && versionBox.Text != shownVersion;
            versionError.Text = string.Empty;
            Refresh();
        };
        versionBox.LostKeyboardFocus += (_, _) =>
        {
            string? why = versionBox.Text == shownVersion ? null : DesignAppDetails.WhyNotVersion(versionBox.Text);
            versionBad = why is not null;
            versionError.Text = why ?? string.Empty;
            Refresh();
        };
        arguments.TextChanged += (_, _) =>
        {
            empty.Visibility = arguments.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            string? why = DesignDocument.WhyNotInputParameters(arguments.Text);
            argumentsBad = why is not null;
            argumentsError.Text = why ?? string.Empty;
            Refresh();
        };
        empty.Visibility = arguments.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        arguments.LostKeyboardFocus += (_, _) =>
        {
            string tidy = DesignDocument.SanitizeInputParameters(arguments.Text);
            if (tidy != arguments.Text)
            {
                arguments.Text = tidy;
            }
        };
        dialog.Loaded += (_, _) => (codeOptions ? (UIElement)arguments : nameBox).Focus();

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        string parameters = DesignDocument.SanitizeInputParameters(arguments.Text);
        bool argumentsChanged = parameters != document.InputParameters && DesignDocument.WhyNotInputParameters(parameters) is null;
        document.SetAppDetails(
            new DesignAppDetailsValues(nameBox.Text, versionBox.Text, authorBox.Text, summaryBox.Text, descriptionBox.Text, mode, thumbnail),
            parameters,
            single.IsChecked == true);
        return argumentsChanged;
    }

    /// <summary>
    /// App Designer's Help Text dialog (<c>HelpCommentsDialogController</c>, <c>HelpCommentsDialogContent</c>): the summary and the
    /// description in the code's font and comment colour, each beside the comment sign that will stand before it, with the
    /// class's name on the summary's line. OK writes them under the class line as one step.
    /// </summary>
    public static void HelpText(Window owner, DesignDocument document)
    {
        (string summaryText, string descriptionText) = document.HelpText;
        var dialog = Dialog(owner, DesignHelpText.Title);
        AutomationProperties.SetAutomationId(dialog, "HelpText");
        var mono = (FontFamily)owner.FindResource(ThemeKeys.FontMono);
        var grid = new Grid { Margin = new Thickness(12), Width = 498 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        TextBlock Comment(string text, int row)
        {
            var sign = new TextBlock { Text = text, FontFamily = mono, FontSize = 12, Foreground = Brushes.ForestGreen, Margin = new Thickness(0, 3, 4, 3) };
            Grid.SetRow(sign, row);
            grid.Children.Add(sign);
            return sign;
        }

        Comment("%" + document.AppName.ToUpperInvariant(), 0);
        Comment(DesignHelpText.DescriptionPrefix, 1);
        var summary = new TextBox { Text = summaryText, FontFamily = mono, FontSize = 12, Foreground = Brushes.ForestGreen, Margin = new Thickness(0, 0, 0, 1) };
        AutomationProperties.SetAutomationId(summary, "HelpText.Summary");
        AutomationProperties.SetName(summary, DesignHelpText.SummaryPlaceholder);
        Grid.SetColumn(summary, 1);
        grid.Children.Add(summary);
        var description = new TextBox { Text = descriptionText, FontFamily = mono, FontSize = 12, Foreground = Brushes.ForestGreen, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetAutomationId(description, "HelpText.Description");
        AutomationProperties.SetName(description, DesignHelpText.DescriptionPlaceholder);
        Grid.SetRow(description, 1);
        Grid.SetColumn(description, 1);
        grid.Children.Add(description);
        void Placeholder(TextBox box, string words, int row)
        {
            var empty = new TextBlock { Text = words, FontFamily = mono, FontSize = 12, Opacity = 0.6, IsHitTestVisible = false, Margin = new Thickness(4, 2, 0, 0) };
            Grid.SetRow(empty, row);
            Grid.SetColumn(empty, 1);
            grid.Children.Add(empty);
            void Show() => empty.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            box.TextChanged += (_, _) => Show();
            Show();
        }

        Placeholder(summary, DesignHelpText.SummaryPlaceholder, 0);
        Placeholder(description, DesignHelpText.DescriptionPlaceholder, 1);
        Button ok = Buttons(dialog, grid, "OK");
        AutomationProperties.SetAutomationId(ok, "HelpText.OK");
        dialog.Loaded += (_, _) => summary.Focus();
        if (dialog.ShowDialog() == true)
        {
            document.SetHelpText(summary.Text, description.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    private static void ShowInvalid(Window owner, string message) => MessageBox.Show(owner, message, "App Designer", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>A data URI's picture, or null when it holds none WPF can draw.</summary>
    private static BitmapSource? ImageOf(string uri)
    {
        if (DesignAppDetails.OfDataUri(uri) is not { } image || image.Bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(image.Bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
