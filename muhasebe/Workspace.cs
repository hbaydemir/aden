using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Aden;

public sealed partial class MainWindow
{
    // Ortak ölçüler: tüm boşluklar 8'in katı.
    private const double Gap = 16;
    private const double SmallGap = 8;
    private const double PagePadding = 24;

    private const string Ink = "#111827";
    private const string Muted = "#6B7280";
    private const string LineColor = "#E5E7EB";

    private sealed record PageInfo(string Title, string Glyph, UIElement Content);
    private readonly List<PageInfo> pages = new();
    private readonly List<Action> refreshers = new();
    private readonly Grid modalLayer = new();
    private readonly StackPanel toastLayer = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 32), IsHitTestVisible = false
    };
    private readonly List<Modal> modals = new();
    private Border sidebar = null!;
    private ListBox navigation = null!;
    private UIElement posPage = null!;
    private UIElement productsPage = null!;
    private bool fullscreen;
    private WindowState restoreState;
    private Action? fullscreenChanged;

    private static SolidColorBrush Paint(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    private static TextBlock Label(string text, double size, string color = Ink, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = Paint(color),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };

    private static TextBlock Glyph(string glyph, double size = 14, string? color = null)
    {
        var icon = new TextBlock
        {
            Text = glyph, FontSize = size, FontFamily = new FontFamily("Segoe MDL2 Assets"),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (color is not null) icon.Foreground = Paint(color);
        return icon;
    }

    private static Border Card(UIElement child, Thickness? padding = null)
    {
        var card = new Border
        {
            Child = child, Padding = padding ?? new Thickness(0), Background = Brushes.White,
            BorderBrush = Paint(LineColor), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        if (child is FrameworkElement element)
            element.SizeChanged += (_, _) => element.Clip = padding is null
                ? new RectangleGeometry(new Rect(element.RenderSize), 11, 11) : null;
        return card;
    }

    /// <summary>Bir eylem butonu. Hatalar uyarı bildirimi olarak gösterilir.</summary>
    private Button Act(string text, Action action, string? style = null, string? glyph = null)
    {
        var button = new Button();
        if (style is not null) button.Style = (Style)FindResource(style);
        SetButtonContent(button, text, glyph);
        button.Click += (_, _) => Run(action);
        return button;
    }

    private static void SetButtonContent(Button button, string text, string? glyph)
    {
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        if (glyph is null) { button.Content = text; return; }
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(Glyph(glyph, 13));
        if (text.Length > 0)
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(SmallGap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { Toast(error.Message, true); }
    }

    private static StackPanel Hstack(params UIElement[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items) Append(row, item);
        return row;
    }

    private static T Append<T>(Panel row, T item) where T : UIElement
    {
        if (item is FrameworkElement element && row.Children.Count > 0)
        {
            element.Margin = row is StackPanel { Orientation: Orientation.Vertical }
                ? new Thickness(0, SmallGap, 0, 0) : new Thickness(SmallGap, 0, 0, 0);
        }
        row.Children.Add(item);
        return item;
    }

    private static UIElement Separator() => new Border
    {
        Width = 1, Height = 22, Background = Paint(LineColor), VerticalAlignment = VerticalAlignment.Center
    };

    private static FrameworkElement Field(string label, FrameworkElement input, bool required = false, UIElement? aside = null)
    {
        var field = new StackPanel();
        var caption = Label(label, 12, "#374151", true);
        if (required) caption.Inlines.Add(new System.Windows.Documents.Run(" *") { Foreground = Paint("#DC2626") });
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        if (aside is not null)
        {
            DockPanel.SetDock(aside, Dock.Right);
            head.Children.Add(aside);
        }
        caption.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(caption);
        field.Children.Add(head);
        field.Children.Add(input);
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        return field;
    }

    private static TextBox Text(string initial = "") => new() { Text = initial };

    private static TextBox Multiline(string initial = "", double height = 88) => new()
    {
        Text = initial, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = height,
        VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(10, 8, 10, 8),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    /// <summary>Eşit sütunlu form ızgarası. span > 1 olan alanlar birden fazla sütun kaplar.</summary>
    private static Grid Form(int columns, params (FrameworkElement Element, int Span)[] fields)
    {
        var grid = new Grid();
        for (var i = 0; i < columns; i++)
        {
            if (i > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        int row = 0, column = 0;
        foreach (var (element, span) in fields)
        {
            if (column + span > columns) { row++; column = 0; }
            // Her form satırı = içerik satırı + (ilk satır hariç) önünde bir boşluk satırı.
            while (grid.RowDefinitions.Count <= row * 2)
            {
                if (grid.RowDefinitions.Count > 0) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Gap) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            Grid.SetRow(element, row * 2);
            Grid.SetColumn(element, column * 2);
            Grid.SetColumnSpan(element, span * 2 - 1);
            grid.Children.Add(element);
            column += span;
        }
        return grid;
    }

    private static (FrameworkElement, int) F(FrameworkElement element, int span = 1) => (element, span);

    private static FrameworkElement SearchField(string placeholder, out TextBox box, bool large = false)
    {
        var host = new Grid();
        box = new TextBox { Style = (Style)Application.Current.FindResource(large ? "PosSearchBox" : "SearchBox") };
        System.Windows.Automation.AutomationProperties.SetName(box, placeholder);
        host.Children.Add(box);
        var icon = Glyph("", large ? 17 : 13, Muted);
        icon.Margin = new Thickness(large ? 16 : 12, 0, 0, 0);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.IsHitTestVisible = false;
        host.Children.Add(icon);
        var hint = Label(placeholder, large ? 17 : 13, "#9CA3AF");
        hint.Margin = new Thickness(large ? 45 : 35, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        host.Children.Add(hint);
        var input = box;
        input.TextChanged += (_, _) => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return host;
    }

    private DataGrid Table()
    {
        var grid = new DataGrid
        {
            IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
            AutoGenerateColumns = true, ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        grid.AutoGeneratingColumn += (_, e) =>
        {
            e.Column.Header = System.Text.RegularExpressions.Regex.Replace(e.PropertyName, "(?<=\\p{Ll})(?=\\p{Lu})", " ");
            if (e.PropertyType == typeof(DateTime) && e.Column is DataGridTextColumn column &&
                column.Binding is System.Windows.Data.Binding binding)
                binding.StringFormat = "dd.MM.yyyy HH:mm";
        };
        return grid;
    }

    private DataGrid View(Func<object[]> data)
    {
        var grid = Table();
        views.Add((grid, data));
        return grid;
    }

    /// <summary>Üstte arama/araç satırı, altta tabloyu dolduran kart.</summary>
    private static Border TableCard(DataGrid grid, UIElement? top = null)
    {
        var dock = new DockPanel();
        if (top is not null)
        {
            var bar = new Border
            {
                Child = top, Padding = new Thickness(Gap, 12, Gap, 12),
                BorderBrush = Paint(LineColor), BorderThickness = new Thickness(0, 0, 0, 1)
            };
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
        }
        dock.Children.Add(grid);
        return Card(dock);
    }

    /// <summary>Sekmeli içerik (segment kontrolü).</summary>
    private static FrameworkElement Segments(out Action<int> select, params (string Title, UIElement Content)[] items)
    {
        var dock = new DockPanel();
        var bar = new Border
        {
            Background = Paint("#F3F4F6"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, Gap)
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Child = buttons;
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        var host = new Grid();
        dock.Children.Add(host);
        var tabs = new List<Button>();
        void Select(int index)
        {
            for (var i = 0; i < items.Length; i++)
            {
                tabs[i].Style = (Style)Application.Current.FindResource(i == index ? "SegmentActiveButton" : "SegmentButton");
                items[i].Content.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        for (var i = 0; i < items.Length; i++)
        {
            var index = i;
            var button = new Button { Content = items[i].Title, Padding = new Thickness(16, 0, 16, 0) };
            if (i > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => Select(index);
            tabs.Add(button);
            buttons.Children.Add(button);
            host.Children.Add(items[i].Content);
        }
        Select(0);
        select = Select;
        return dock;
    }

    private DockPanel AddPage(string title, string subtitle, string glyph, out StackPanel actions)
    {
        var root = new DockPanel { Margin = new Thickness(PagePadding) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, PagePadding) };
        actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Label(title, 22, bold: true));
        var sub = Label(subtitle, 13, Muted);
        sub.Margin = new Thickness(0, 4, 0, 0);
        titles.Children.Add(sub);
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var body = new DockPanel();
        root.Children.Add(body);
        AddTab(title, glyph, root);
        return body;
    }

    private UIElement AddTab(string title, string glyph, UIElement content)
    {
        pages.Add(new(title, glyph, content));
        return content;
    }

    private void SelectPage(UIElement page) => navigation.SelectedIndex = pages.FindIndex(p => p.Content == page);

    private void BuildWorkspace()
    {
        Icon = Brand.Mark;
        Width = 1370;
        Height = 880;
        MinWidth = 1024;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Paint("#F4F6FA");
        Foreground = Paint(Ink);

        var root = new Grid();
        Content = root;
        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        workspace.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(workspace);
        root.Children.Add(modalLayer);
        root.Children.Add(toastLayer);

        var rail = new DockPanel { Margin = new Thickness(Gap, PagePadding, Gap, Gap) };
        sidebar = new Border
        {
            Child = rail, Background = Brushes.White, Width = 232,
            BorderThickness = new Thickness(0, 0, 1, 0), BorderBrush = Paint(LineColor)
        };
        workspace.Children.Add(sidebar);
        var brand = new Image
        {
            Source = Brand.Wordmark, Height = 36, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, PagePadding)
        };
        DockPanel.SetDock(brand, Dock.Top);
        rail.Children.Add(brand);

        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        rail.Children.Add(footer);
        if (updateCard is not null) footer.Children.Add(updateCard);
        var statusRow = new DockPanel { Margin = new Thickness(12, Gap, 12, 0) };
        var dot = new Border
        {
            Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Paint("#22C55E"),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, SmallGap, 0)
        };
        statusRow.Children.Add(dot);
        status.FontSize = 11;
        status.Foreground = Paint(Muted);
        status.Margin = new Thickness(0);
        status.TextWrapping = TextWrapping.Wrap;
        status.MaxHeight = 48;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        statusRow.Children.Add(status);
        footer.Children.Add(statusRow);

        navigation = new ListBox
        {
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            ItemContainerStyle = (Style)FindResource("NavItem")
        };
        ScrollViewer.SetPanningMode(navigation, PanningMode.VerticalOnly);
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled);
        foreach (var page in pages)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Glyph(page.Glyph, 15);
            icon.Width = 28;
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = page.Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            navigation.Items.Add(row);
        }
        rail.Children.Add(navigation);

        Grid.SetColumn(host, 1);
        workspace.Children.Add(host);

        navigation.SelectionChanged += (_, _) =>
        {
            if (navigation.SelectedIndex < 0) return;
            host.Content = pages[navigation.SelectedIndex].Content;
            if (fullscreen && host.Content != posPage) SetFullscreen(false);
        };
        SelectPage(posPage);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && modals.Count > 0)
            {
                e.Handled = true;
                modals[^1].RequestClose();
            }
            else if (e.Key == Key.Escape && fullscreen)
            {
                e.Handled = true;
                SetFullscreen(false);
            }
            else if (e.Key == Key.F11 && host.Content == posPage)
            {
                e.Handled = true;
                SetFullscreen(!fullscreen);
            }
        };
    }

    private void SetFullscreen(bool on)
    {
        if (fullscreen == on) return;
        fullscreen = on;
        sidebar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on)
        {
            restoreState = WindowState;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = restoreState;
        }
        fullscreenChanged?.Invoke();
    }

    private static ScrollViewer TouchScroll(UIElement child) =>
        new() { Content = child, PanningMode = PanningMode.VerticalOnly, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    // ───────────── Modal sistemi ─────────────

    private sealed class Modal
    {
        public required Action Close { get; init; }
        public required StackPanel Footer { get; init; }
        public Action RequestClose { get; set; } = () => { };
        public Action? Closed { get; set; }
    }

    private Modal ShowModal(string title, UIElement body, double width = 520, double? height = null, string? subtitle = null)
    {
        var previous = Keyboard.FocusedElement as IInputElement;
        var backdrop = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x73, 0x11, 0x18, 0x27)) };
        var card = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(16), Width = width,
            Margin = new Thickness(32), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Opacity = 0.18 }
        };
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        backdrop.Children.Add(card);
        var dock = new DockPanel();
        card.Child = dock;

        var header = new DockPanel { Margin = new Thickness(PagePadding, 20, Gap, 0) };
        var close = new Button
        {
            Style = (Style)FindResource("GhostButton"), Width = 34, Padding = new Thickness(0),
            Content = Glyph("", 12), VerticalAlignment = VerticalAlignment.Top
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "Kapat");
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var titles = new StackPanel();
        titles.Children.Add(Label(title, 18, bold: true));
        if (subtitle is not null)
        {
            var sub = Label(subtitle, 13, Muted);
            sub.Margin = new Thickness(0, 4, 0, 0);
            sub.TextWrapping = TextWrapping.Wrap;
            titles.Children.Add(sub);
        }
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var footerBar = new Border
        {
            Child = footer, Padding = new Thickness(PagePadding, Gap, PagePadding, Gap),
            BorderBrush = Paint(LineColor), BorderThickness = new Thickness(0, 1, 0, 0)
        };
        DockPanel.SetDock(footerBar, Dock.Bottom);
        dock.Children.Add(footerBar);
        dock.Children.Add(new ScrollViewer
        {
            Content = body, Padding = new Thickness(PagePadding, 20, PagePadding, PagePadding),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly
        });

        void Fit()
        {
            if (height is double desired) card.Height = Math.Max(240, Math.Min(desired, modalLayer.ActualHeight - 64));
            card.Width = Math.Min(width, Math.Max(320, modalLayer.ActualWidth - 64));
        }
        SizeChangedEventHandler resize = (_, _) => Fit();
        modalLayer.SizeChanged += resize;
        Fit();

        Modal? modal = null;
        modal = new Modal
        {
            Footer = footer,
            Close = () =>
            {
                if (!modals.Remove(modal!)) return;
                modalLayer.Children.Remove(backdrop);
                modalLayer.SizeChanged -= resize;
                modal!.Closed?.Invoke();
                if (modals.Count == 0) (previous ?? saleSearch)?.Focus();
            }
        };
        modal.RequestClose = modal.Close;
        close.Click += (_, _) => modal.RequestClose();
        modals.Add(modal);
        modalLayer.Children.Add(backdrop);
        card.Loaded += (_, _) =>
        {
            var first = FindFirst<TextBox>(body) as UIElement ?? FindFirst<ComboBox>(body);
            if (first is not null) first.Focus(); else card.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        return modal;
    }

    private static T? FindFirst<T>(DependencyObject root) where T : UIElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && match.IsVisible && match.IsEnabled && match.Focusable &&
                !(match is TextBox { IsReadOnly: true })) return match;
            if (child is ComboBox or DatePicker) continue;
            var nested = FindFirst<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private void Confirm(string title, string message, string action, Action yes, bool danger = true)
    {
        var text = Label(message, 14, "#374151");
        text.TextWrapping = TextWrapping.Wrap;
        var modal = ShowModal(title, text, 420);
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        var ok = Append(modal.Footer, Act(action, () => { modal.Close(); yes(); }, danger ? "DangerSolidButton" : "PrimaryButton"));
        modal.Footer.Loaded += (_, _) => ok.Focus();
    }

    private void ConfirmDelete(string item, Action yes) =>
        Confirm("Silme onayı", $"“{item}” silinecek. Bu işlem geri alınamaz.", "Sil", yes);

    // ───────────── Bildirimler ─────────────

    private void Toast(string message, bool error = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(error ? "" : "", 14, error ? "#F87171" : "#4ADE80"));
        var text = new TextBlock
        {
            Text = message, Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(10, 0, 0, 0),
            MaxWidth = 520, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(text);
        var toast = new Border
        {
            Child = row, Background = Paint("#1F2937"), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(Gap, 12, 20, 12), Margin = new Thickness(0, SmallGap, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Opacity = 0.2 }
        };
        while (toastLayer.Children.Count >= 3) toastLayer.Children.RemoveAt(0);
        toastLayer.Children.Add(toast);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(error ? 5 : 3) };
        timer.Tick += (_, _) => { timer.Stop(); toastLayer.Children.Remove(toast); };
        timer.Start();
    }
}
