using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aden;

/// <summary>Ürün kategorileri, ürün fotoğrafları ve otomatik kod üretimi.</summary>
public sealed partial class MainWindow
{
    private const string GeneralCategory = "genel";
    /// <summary>Barkodsuz ürünler için varsayılan EAN-13 yer tutucusu. Benzersizlik ve aramada yok sayılır.</summary>
    private const string DefaultBarcode = "0000000000001";

    private readonly Dictionary<string, ProductCategory> categories = new(StringComparer.OrdinalIgnoreCase);

    private void LoadCategories()
    {
        categories.Clear();
        foreach (var item in Read<ProductCategory>("category"))
        {
            if (item.Value.Archived) categories.Remove(item.Value.Id);
            else categories[item.Value.Id] = item.Value;
        }
        categories[GeneralCategory] = new(GeneralCategory, "Genel", "");
    }

    private ProductCategory CategoryOf(Product product) =>
        product.Category.Length > 0 && categories.TryGetValue(product.Category, out var category)
            ? category : categories[GeneralCategory];

    private string ParentOf(ProductCategory category) =>
        category.Parent.Length > 0 && categories.ContainsKey(category.Parent) ? category.Parent : "";

    private string CategoryPath(string id)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (id.Length > 0 && categories.TryGetValue(id, out var category) && seen.Add(id))
        {
            names.Insert(0, category.Name);
            id = ParentOf(category);
        }
        return string.Join(" › ", names);
    }

    /// <summary>Kategoriler ağaç sırasında (önce Genel, sonra ada göre), derinlikleriyle.</summary>
    private List<(ProductCategory Category, int Depth)> CategoryTree()
    {
        var result = new List<(ProductCategory, int)>();
        var compare = StringComparer.Create(culture, true);
        var children = categories.Values.GroupBy(ParentOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id == GeneralCategory ? 0 : 1).ThenBy(c => c.Name, compare).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string parent, int depth)
        {
            foreach (var child in children.GetValueOrDefault(parent) ?? [])
            {
                if (!visited.Add(child.Id)) continue;
                result.Add((child, depth));
                Walk(child.Id, depth + 1);
            }
        }
        Walk("", 0);
        // Döngüye girmiş (bozuk) kayıtlar da listede görünsün.
        foreach (var orphan in categories.Values.Where(c => !visited.Contains(c.Id))) result.Add((orphan, 0));
        return result;
    }

    private bool IsDescendant(string id, string ancestor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (id.Length > 0 && seen.Add(id) && categories.TryGetValue(id, out var category))
        {
            if (category.Id.Equals(ancestor, StringComparison.OrdinalIgnoreCase)) return true;
            id = ParentOf(category);
        }
        return false;
    }

    private sealed record CategoryChoice(string Id, string Label);

    private ComboBox CategoryBox(string? selected)
    {
        var box = new ComboBox { DisplayMemberPath = nameof(CategoryChoice.Label), MaxDropDownHeight = 320 };
        void Fill(string? id)
        {
            box.ItemsSource = CategoryTree().Select(c => new CategoryChoice(c.Category.Id, CategoryPath(c.Category.Id))).ToArray();
            box.SelectedItem = box.Items.Cast<CategoryChoice>().FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? box.Items.Cast<CategoryChoice>().First(c => c.Id == GeneralCategory);
        }
        Fill(selected is { Length: > 0 } ? selected : GeneralCategory);
        Action refill = () => Fill((box.SelectedItem as CategoryChoice)?.Id);
        refreshers.Add(refill);
        box.Unloaded += (_, _) => refreshers.Remove(refill);
        box.Loaded += (_, _) => { if (!refreshers.Contains(refill)) refreshers.Add(refill); };
        return box;
    }

    // ───────────── Kategori yönetimi ─────────────

    private void ShowCategories()
    {
        var root = new StackPanel();
        var name = Text();
        var parent = new ComboBox { DisplayMemberPath = nameof(CategoryChoice.Label), MaxDropDownHeight = 320 };
        var add = Act("Ekle", () => AddCategory(), "SuccessButton", "");
        add.VerticalAlignment = VerticalAlignment.Bottom;
        var addRow = new Grid();
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallGap) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition());
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SmallGap) });
        addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        addRow.Children.Add(Field("Yeni kategori adı", name));
        var parentField = Field("Üst kategori", parent);
        Grid.SetColumn(parentField, 2);
        addRow.Children.Add(parentField);
        Grid.SetColumn(add, 4);
        addRow.Children.Add(add);
        root.Children.Add(addRow);
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(AddCategory); } };

        var hint = Label("Bir kategoriyi sürükleyip başka bir kategorinin üstüne bırakırsan onun alt kategorisi olur. " +
            "Ana kategori yapmak için aşağıdaki alana bırak.", 12, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, PagePadding, 0, SmallGap);
        root.Children.Add(hint);

        var rootZone = new Border
        {
            CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5), Padding = new Thickness(Gap, 12, Gap, 12),
            BorderBrush = Paint("#CBD5E1"), Background = Paint("#F9FAFB"), AllowDrop = true, Margin = new Thickness(0, 0, 0, SmallGap)
        };
        var zoneText = Hstack(Glyph("", 14, Muted), Label("Buraya bırak: ana kategori yap", 13, Muted, true));
        zoneText.HorizontalAlignment = HorizontalAlignment.Center;
        rootZone.Child = zoneText;
        root.Children.Add(rootZone);

        var list = new StackPanel();
        root.Children.Add(Card(list));

        var modal = ShowModal("Ürün kategorileri", root, 640, 720,
            "\"Genel\" varsayılan kategoridir; silinemez ve taşınamaz.");
        Append(modal.Footer, Act("Kapat", modal.Close, "PrimaryButton"));
        refreshers.Add(Render);
        modal.Closed = () => refreshers.Remove(Render);
        Render();

        void Render()
        {
            var selected = (parent.SelectedItem as CategoryChoice)?.Id ?? "";
            var choices = new List<CategoryChoice> { new("", "— Ana kategori —") };
            choices.AddRange(CategoryTree().Where(c => c.Category.Id != GeneralCategory)
                .Select(c => new CategoryChoice(c.Category.Id, CategoryPath(c.Category.Id))));
            parent.ItemsSource = choices;
            parent.SelectedItem = choices.FirstOrDefault(c => c.Id == selected) ?? choices[0];

            list.Children.Clear();
            var counts = products.Values.GroupBy(p => CategoryOf(p).Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var tree = CategoryTree();
            for (var i = 0; i < tree.Count; i++)
                list.Children.Add(Row(tree[i].Category, tree[i].Depth, counts.GetValueOrDefault(tree[i].Category.Id), i == tree.Count - 1));
        }

        UIElement Row(ProductCategory category, int depth, int count, bool last)
        {
            var locked = category.Id == GeneralCategory;
            var row = new DockPanel { Margin = new Thickness(depth * 28, 0, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            if (locked)
            {
                var badge = new Border
                {
                    Background = Paint("#F3F4F6"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3),
                    Child = Hstack(Glyph("", 11, Muted), Label("Varsayılan", 11, Muted, true))
                };
                actions.Children.Add(badge);
            }
            else
            {
                var edit = Act("", () => Rename(category), "GhostButton", "");
                edit.Width = 34;
                edit.Padding = new Thickness(0);
                edit.Foreground = Paint("#2563EB");
                edit.ToolTip = "Yeniden adlandır";
                System.Windows.Automation.AutomationProperties.SetName(edit, $"{category.Name} düzenle");
                actions.Children.Add(edit);
                var delete = Act("", () => Delete(category), "GhostButton", "");
                delete.Width = 34;
                delete.Padding = new Thickness(0);
                delete.Foreground = Paint("#DC2626");
                delete.ToolTip = "Sil";
                System.Windows.Automation.AutomationProperties.SetName(delete, $"{category.Name} sil");
                actions.Children.Add(delete);
            }
            var grip = Glyph(locked ? "" : "", 13, locked ? "#D1D5DB" : "#9CA3AF");
            grip.Width = 24;
            row.Children.Add(grip);
            var icon = Glyph("", 15, depth == 0 ? "#D97706" : "#9CA3AF");
            icon.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(icon);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Label(category.Name, 14, bold: depth == 0));
            var meta = Label($"{count} ürün", 12, Muted);
            meta.Margin = new Thickness(SmallGap, 0, 0, 0);
            meta.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(meta);
            row.Children.Add(title);

            var item = new Border
            {
                Child = row, Padding = new Thickness(12, SmallGap, SmallGap, SmallGap), MinHeight = 48,
                Background = Brushes.White, BorderBrush = Paint("#F0F1F4"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1),
                AllowDrop = !locked, Cursor = locked ? Cursors.Arrow : Cursors.SizeAll
            };
            if (!locked) Draggable(item, category.Id);
            if (!locked)
            {
                item.DragOver += (_, e) =>
                {
                    var valid = e.Data.GetData(typeof(string)) is string source && source != category.Id && !IsDescendant(category.Id, source);
                    e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
                    item.Background = Paint(valid ? "#EFF4FF" : "#FEF2F2");
                    e.Handled = true;
                };
                item.DragLeave += (_, _) => item.Background = Brushes.White;
                item.Drop += (_, e) =>
                {
                    item.Background = Brushes.White;
                    if (e.Data.GetData(typeof(string)) is string source) Run(() => Move(source, category.Id));
                    e.Handled = true;
                };
            }
            return item;
        }

        void Draggable(Border item, string id)
        {
            Point? start = null;
            item.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(item);
            item.PreviewMouseLeftButtonUp += (_, _) => start = null;
            item.MouseMove += (_, e) =>
            {
                if (start is not Point origin || e.LeftButton != MouseButtonState.Pressed) return;
                var delta = e.GetPosition(item) - origin;
                if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                start = null;
                item.Opacity = 0.5;
                DragDrop.DoDragDrop(item, new DataObject(typeof(string), id), DragDropEffects.Move);
                item.Opacity = 1;
            };
        }

        rootZone.DragOver += (_, e) =>
        {
            var valid = e.Data.GetData(typeof(string)) is string;
            e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
            rootZone.BorderBrush = Paint(valid ? "#2563EB" : "#CBD5E1");
            rootZone.Background = Paint(valid ? "#EFF4FF" : "#F9FAFB");
            e.Handled = true;
        };
        rootZone.DragLeave += (_, _) => { rootZone.BorderBrush = Paint("#CBD5E1"); rootZone.Background = Paint("#F9FAFB"); };
        rootZone.Drop += (_, e) =>
        {
            rootZone.BorderBrush = Paint("#CBD5E1");
            rootZone.Background = Paint("#F9FAFB");
            if (e.Data.GetData(typeof(string)) is string source) Run(() => Move(source, ""));
            e.Handled = true;
        };

        void AddCategory()
        {
            var title = name.Text.Trim();
            if (title.Length == 0) throw new InvalidOperationException("Kategori adını yaz.");
            var parentId = (parent.SelectedItem as CategoryChoice)?.Id ?? "";
            EnsureUniqueName(title, parentId, null);
            Save("category", new ProductCategory(Guid.NewGuid().ToString("N")[..12], title, parentId));
            name.Clear();
            name.Focus();
            Toast($"\"{title}\" kategorisi eklendi.");
        }

        void Move(string id, string target)
        {
            if (!categories.TryGetValue(id, out var category) || id == GeneralCategory) return;
            if (target == id || (target.Length > 0 && IsDescendant(target, id)))
                throw new InvalidOperationException("Kategori kendi alt kategorisine taşınamaz.");
            if (ParentOf(category).Equals(target, StringComparison.OrdinalIgnoreCase)) return;
            EnsureUniqueName(category.Name, target, id);
            Save("category", category with { Parent = target });
            Toast(target.Length == 0 ? $"\"{category.Name}\" ana kategori yapıldı."
                : $"\"{category.Name}\" → \"{categories[target].Name}\" altına taşındı.");
        }

        void Rename(ProductCategory category)
        {
            var field = Text(category.Name);
            var dialog = ShowModal("Kategoriyi yeniden adlandır", Field("Kategori adı", field), 420);
            Append(dialog.Footer, Act("Vazgeç", dialog.Close));
            Append(dialog.Footer, Act("Kaydet", () =>
            {
                var title = field.Text.Trim();
                if (title.Length == 0) throw new InvalidOperationException("Kategori adını yaz.");
                EnsureUniqueName(title, ParentOf(category), category.Id);
                Save("category", category with { Name = title });
                dialog.Close();
                Toast("Kategori güncellendi.");
            }, "PrimaryButton", ""));
            field.SelectAll();
        }

        void Delete(ProductCategory category)
        {
            var children = categories.Values.Where(c => ParentOf(c).Equals(category.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var used = products.Values.Count(p => CategoryOf(p).Id == category.Id);
            var detail = (children.Length > 0 ? $" {children.Length} alt kategori bir üst seviyeye taşınacak." : "") +
                (used > 0 ? $" Bu kategorideki {used} ürün \"Genel\" kategorisine geçecek." : "");
            Confirm("Kategoriyi sil", $"\"{category.Name}\" silinecek.{detail}", "Sil", () =>
            {
                foreach (var child in children) store.Add("category", child with { Parent = ParentOf(category) });
                foreach (var product in products.Values.Where(p => CategoryOf(p).Id == category.Id).ToArray())
                    store.Add("product", product with { Category = GeneralCategory });
                Save("category", category with { Archived = true });
                Toast("Kategori silindi.");
            });
        }

        void EnsureUniqueName(string title, string parentId, string? self)
        {
            if (title.Length > 60) throw new InvalidOperationException("Kategori adı en fazla 60 karakter olabilir.");
            if (categories.Values.Any(c => c.Id != self && ParentOf(c).Equals(parentId, StringComparison.OrdinalIgnoreCase) &&
                    string.Compare(c.Name, title, culture, CompareOptions.IgnoreCase) == 0))
                throw new InvalidOperationException("Aynı seviyede bu adla bir kategori zaten var.");
        }
    }

    // ───────────── Kodlar ─────────────

    /// <summary>Önekle başlayan en büyük numaranın bir fazlası: C-0001, U-0001…</summary>
    private static string NextCode(string prefix, IEnumerable<string> used)
    {
        var next = used.Select(code => code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    ? number : 0)
            .DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:0000}";
    }

    private string NextAccountCode() => NextCode("C-", Read<Account>("account").Select(a => a.Value.Code));
    private string NextProductCode() => NextCode("U-", Read<Product>("product").Select(p => p.Value.Code));

    // ───────────── Fotoğraflar ─────────────

    private readonly Dictionary<(string, int), ImageSource> photoCache = new();
    private string PhotoFolder => Path.Combine(store.Folder, "Fotograflar");

    /// <summary>Fotoğrafı beyaz zemine oturtur, en fazla 1280 piksele küçültür ve JPEG olarak saklar.</summary>
    private string ImportPhoto(string file)
    {
        BitmapSource source;
        int orientation;
        using (var stream = File.OpenRead(file))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            source = decoder.Frames[0];
            orientation = 1;
            try
            {
                if (source.Metadata is BitmapMetadata metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value)
                    orientation = value;
            }
            catch (NotSupportedException) { }
            catch (ArgumentException) { }
        }
        var rotate = orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 };
        if (rotate != 0) source = new TransformedBitmap(source, new RotateTransform(rotate));

        var scale = Math.Min(1, 1280.0 / Math.Max(source.PixelWidth, source.PixelHeight));
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(0, 0, width, height));
        }
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 86 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var bytes = memory.ToArray();
        var fileName = Convert.ToHexString(SHA256.HashData(bytes))[..24].ToLowerInvariant() + ".jpg";
        Directory.CreateDirectory(PhotoFolder);
        var target = Path.Combine(PhotoFolder, fileName);
        if (!File.Exists(target)) File.WriteAllBytes(target, bytes);
        return fileName;
    }

    /// <summary>Kayıtlı fotoğraf; yoksa veya okunamıyorsa "NO IMAGE" görseli.</summary>
    private ImageSource Photo(string? name, int width)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return NoImage;
        if (photoCache.TryGetValue((name, width), out var cached)) return cached;
        var path = Path.Combine(PhotoFolder, name);
        if (!File.Exists(path)) return NoImage;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = width;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return photoCache[(name, width)] = image;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            return NoImage;
        }
    }

    private static string? CoverOf(Product product) =>
        product.Photos is { Length: > 0 } photos ? (photos.Contains(product.Cover) ? product.Cover : photos[0]) : null;

    /// <summary>Gri tonlarda, sektörden bağımsız koli simgesi ve "NO IMAGE" yazısı.</summary>
    private static readonly ImageSource NoImage = CreateNoImage();

    private static DrawingImage CreateNoImage()
    {
        var group = new DrawingGroup();
        void Fill(string color, Geometry geometry) => group.Children.Add(new GeometryDrawing(Paint(color), null, geometry));
        static Geometry Poly(params double[] xy)
        {
            var figure = new PathFigure { StartPoint = new Point(xy[0], xy[1]), IsClosed = true, IsFilled = true };
            for (var i = 2; i < xy.Length; i += 2) figure.Segments.Add(new LineSegment(new Point(xy[i], xy[i + 1]), false));
            return new PathGeometry([figure]);
        }

        Fill("#F1F2F4", new RectangleGeometry(new Rect(0, 0, 240, 240)));
        // Koli: üst, sol ve sağ yüzler
        Fill("#D9DCE1", Poly(120, 46, 176, 70, 120, 94, 64, 70));
        Fill("#C6CAD1", Poly(64, 70, 120, 94, 120, 156, 64, 132));
        Fill("#B7BCC4", Poly(176, 70, 120, 94, 120, 156, 176, 132));
        // Bant
        Fill("#E8EAEE", Poly(85, 54, 99, 48, 155, 72, 141, 78));
        Fill("#D3D7DD", Poly(141, 78, 155, 72, 155, 96, 141, 102));
        // Yüzler arası ince ayrım
        group.Children.Add(new GeometryDrawing(null, new Pen(Paint("#F1F2F4"), 2.5), new LineGeometry(new Point(120, 94), new Point(120, 156))));
        group.Children.Add(new GeometryDrawing(null, new Pen(Paint("#F1F2F4"), 2.5),
            new PathGeometry([new PathFigure(new Point(64, 70), [new LineSegment(new Point(120, 94), true), new LineSegment(new Point(176, 70), true)], false)])));

        var text = new FormattedText("N O   I M A G E", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 15,
            Brushes.Black, 1.0);
        Fill("#A3A9B3", text.BuildGeometry(new Point(120 - text.Width / 2, 178)));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Fotoğraf yöneticisi: çoklu ekleme, silme ve kapak seçimi.</summary>
    private FrameworkElement PhotoEditor(List<string> photos, Func<string> getCover, Action<string> setCover)
    {
        var panel = new WrapPanel();
        var root = new Border { Child = panel, AllowDrop = true, Background = Brushes.Transparent };
        const double size = 108;

        void Render()
        {
            panel.Children.Clear();
            foreach (var photo in photos) panel.Children.Add(Thumb(photo));
            var add = new Button
            {
                Width = size, Height = size, Margin = new Thickness(0, 0, SmallGap, SmallGap), Background = Paint("#F9FAFB"),
                BorderBrush = Paint("#CBD5E1")
            };
            var addContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var plus = Glyph("", 18, "#2563EB");
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            addContent.Children.Add(plus);
            var caption = Label("Fotoğraf ekle", 12, "#2563EB", true);
            caption.Margin = new Thickness(0, SmallGap, 0, 0);
            addContent.Children.Add(caption);
            add.Content = addContent;
            System.Windows.Automation.AutomationProperties.SetName(add, "Fotoğraf ekle");
            add.Click += (_, _) => Run(Pick);
            panel.Children.Add(add);
        }

        UIElement Thumb(string photo)
        {
            var isCover = photo == getCover();
            var grid = new Grid { Width = size, Height = size, Margin = new Thickness(0, 0, SmallGap, SmallGap) };
            var image = new Image { Source = Photo(photo, 240), Stretch = Stretch.UniformToFill };
            var frame = new Border
            {
                Child = image, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(isCover ? 2 : 1),
                BorderBrush = Paint(isCover ? "#2563EB" : LineColor), Background = Paint("#F3F4F6")
            };
            image.SizeChanged += (_, _) => image.Clip = new RectangleGeometry(new Rect(image.RenderSize), 9, 9);
            grid.Children.Add(frame);

            Button Overlay(string glyph, string tip, string color, HorizontalAlignment align, Action action)
            {
                var button = new Button
                {
                    Width = 28, Height = 28, MinHeight = 28, Padding = new Thickness(0), Margin = new Thickness(6),
                    HorizontalAlignment = align, VerticalAlignment = VerticalAlignment.Top,
                    Background = Paint("#F2FFFFFF"), BorderBrush = Paint("#1A000000"), Content = Glyph(glyph, 12, color), ToolTip = tip
                };
                System.Windows.Automation.AutomationProperties.SetName(button, tip);
                button.Click += (_, _) => Run(action);
                grid.Children.Add(button);
                return button;
            }
            Overlay(isCover ? "" : "", isCover ? "Kapak fotoğrafı" : "Kapak fotoğrafı yap", isCover ? "#F59E0B" : "#6B7280",
                HorizontalAlignment.Left, () => { setCover(photo); Render(); });
            Overlay("", "Fotoğrafı kaldır", "#DC2626", HorizontalAlignment.Right, () =>
            {
                photos.Remove(photo);
                if (getCover() == photo) setCover(photos.FirstOrDefault() ?? "");
                Render();
            });
            if (isCover)
            {
                var badge = new Border
                {
                    Background = Paint("#2563EB"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 2, 8, 3),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(6), Child = Label("Kapak", 11, "#FFFFFF", true), IsHitTestVisible = false
                };
                grid.Children.Add(badge);
            }
            return grid;
        }

        void Import(IEnumerable<string> files)
        {
            var failed = 0;
            foreach (var file in files)
            {
                try
                {
                    var name = ImportPhoto(file);
                    if (!photos.Contains(name)) photos.Add(name);
                }
                catch (Exception error) when (error is IOException or NotSupportedException or UnauthorizedAccessException or
                    FileFormatException or ArgumentException or InvalidOperationException)
                {
                    failed++;
                }
            }
            if (getCover().Length == 0 && photos.Count > 0) setCover(photos[0]);
            Render();
            if (failed > 0) Toast($"{failed} dosya fotoğraf olarak okunamadı.", true);
        }

        void Pick()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Ürün fotoğrafları", Multiselect = true,
                Filter = "Görseller (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff"
            };
            if (dialog.ShowDialog(this) == true) Import(dialog.FileNames);
        }

        root.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        root.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Import(files);
            e.Handled = true;
        };
        Render();
        return root;
    }
}
