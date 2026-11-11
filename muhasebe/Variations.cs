using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Aden;

/// <summary>Varyasyon tipleri (Renk, Beden…), varyantları ve ürün birimleri.</summary>
public sealed partial class MainWindow
{
    private static readonly string[] Units =
    [
        "Adet", "Metre", "Santimetre", "Metrekare", "Metreküp", "Gram", "Kilogram", "Ton", "Litre", "Mililitre",
        "Kutu", "Koli", "Paket", "Palet", "Poşet", "Çuval", "Rulo", "Top", "Takım", "Set", "Çift", "Düzine", "Şişe", "Teneke", "Bidon"
    ];

    private static readonly VariationType[] DefaultVariations =
    [
        new("renk", "Renk", ["Siyah", "Beyaz", "Gri", "Kırmızı", "Mavi", "Lacivert", "Yeşil", "Sarı", "Kahverengi"]),
        new("beden", "Beden", ["XS", "S", "M", "L", "XL", "XXL"]),
        new("numara", "Numara", ["36", "37", "38", "39", "40", "41", "42", "43", "44", "45"]),
        new("boyut", "Boyut", ["Küçük", "Orta", "Büyük"]),
        new("materyal", "Materyal", ["Pamuk", "Polyester", "Deri", "Metal", "Plastik", "Ahşap"]),
        new("hacim", "Hacim", ["250 ml", "500 ml", "1 L", "5 L"]),
        new("agirlik", "Ağırlık", ["100 g", "250 g", "500 g", "1 kg"])
    ];

    private readonly Dictionary<string, VariationType> variations = new(StringComparer.OrdinalIgnoreCase);

    private void LoadVariations()
    {
        variations.Clear();
        foreach (var item in DefaultVariations) variations[item.Id] = item;
        foreach (var item in Read<VariationType>("variation"))
        {
            if (item.Value.Archived) variations.Remove(item.Value.Id);
            else variations[item.Value.Id] = item.Value;
        }
    }

    /// <summary>Ürünün satışta seçilebilir varyantları (tip silinmişse boş).</summary>
    private string[] OptionsOf(Product product) =>
        product.Variation.Length > 0 && variations.TryGetValue(product.Variation, out var type)
            ? (product.Options is { Length: > 0 } chosen ? chosen : type.Values) : [];

    private string VariationName(Product product) =>
        product.Variation.Length > 0 && variations.TryGetValue(product.Variation, out var type) ? type.Name : "";

    private sealed record VariationChoice(string Id, string Label);

    /// <summary>Ürün kartındaki varyasyon alanı: tip seçimi ve satışta sunulacak varyantlar.</summary>
    private (FrameworkElement View, Func<(string Variation, string[]? Options)> Read) VariationEditor(Product? product)
    {
        var root = new StackPanel();
        var type = new ComboBox { DisplayMemberPath = nameof(VariationChoice.Label), MaxDropDownHeight = 320 };
        var manage = Act("Varyasyonları yönet", () => ShowVariations(), glyph: "");
        manage.Margin = new Thickness(SmallGap, 0, 0, 0);
        var row = new DockPanel();
        DockPanel.SetDock(manage, Dock.Right);
        row.Children.Add(manage);
        row.Children.Add(type);
        root.Children.Add(row);
        var values = new WrapPanel { Margin = new Thickness(0, SmallGap, 0, 0) };
        root.Children.Add(values);
        var hint = Label("", 12, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(hint);
        var chosen = new HashSet<string>(product?.Options ?? [], StringComparer.OrdinalIgnoreCase);
        var selectAll = product?.Options is null;

        void Fill(string id)
        {
            var items = new List<VariationChoice> { new("", "Varyasyon yok") };
            items.AddRange(variations.Values.OrderBy(v => v.Name, StringComparer.Create(culture, true)).Select(v => new VariationChoice(v.Id, v.Name)));
            type.ItemsSource = items;
            type.SelectedItem = items.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? items[0];
        }
        void RenderValues()
        {
            values.Children.Clear();
            var id = (type.SelectedItem as VariationChoice)?.Id ?? "";
            if (id.Length == 0 || !variations.TryGetValue(id, out var variation))
            {
                hint.Text = "Varyasyon yok: ürün tek seçenekle satılır. İstersen Renk, Beden gibi bir tip seçebilirsin.";
                return;
            }
            if (selectAll) { chosen.Clear(); chosen.UnionWith(variation.Values); selectAll = false; }
            foreach (var value in variation.Values)
            {
                var box = new CheckBox
                {
                    Content = value, IsChecked = chosen.Contains(value), Margin = new Thickness(0, 0, Gap, SmallGap), MinHeight = 0
                };
                box.Checked += (_, _) => chosen.Add(value);
                box.Unchecked += (_, _) => chosen.Remove(value);
                values.Children.Add(box);
            }
            hint.Text = variation.Values.Length == 0 ? "Bu tipte varyant yok. \"Varyasyonları yönet\" ile varyant ekle."
                : "Satışta işaretli varyantlardan biri seçilir. Stok ürün toplamı üzerinden tutulur.";
        }
        type.SelectionChanged += (_, _) =>
        {
            if (type.SelectedItem is VariationChoice { Id.Length: > 0 } && chosen.Count == 0) selectAll = true;
            RenderValues();
        };
        Fill(product?.Variation ?? "");
        RenderValues();
        Action refill = () => { Fill((type.SelectedItem as VariationChoice)?.Id ?? ""); RenderValues(); };
        refreshers.Add(refill);
        root.Unloaded += (_, _) => refreshers.Remove(refill);
        root.Loaded += (_, _) => { if (!refreshers.Contains(refill)) refreshers.Add(refill); };

        return (root, () =>
        {
            var id = (type.SelectedItem as VariationChoice)?.Id ?? "";
            if (id.Length == 0 || !variations.TryGetValue(id, out var variation)) return ("", null);
            var options = variation.Values.Where(chosen.Contains).ToArray();
            if (options.Length == 0) throw new InvalidOperationException("En az bir varyant işaretle veya \"Varyasyon yok\" seç.");
            return (variation.Id, options.Length == variation.Values.Length ? null : options);
        });
    }

    /// <summary>Varyasyon tipi ve varyant yönetimi.</summary>
    private void ShowVariations()
    {
        var root = new StackPanel();
        var name = Text();
        name.MaxLength = 60;
        root.Children.Add(Field("Yeni varyasyon tipi", InputRow(AddType, "Varyasyon tipi ekle", (Hinted(name, "Örn. Desen, Kapasite"), new GridLength(1, GridUnitType.Star)))));
        name.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(AddType); } };
        var list = new StackPanel { Margin = new Thickness(0, Gap, 0, 0) };
        root.Children.Add(list);
        var modal = ShowModal("Varyasyonlar", root, 680, 760, "Renk, beden gibi tipleri ve varyantlarını düzenle. Gereksiz gördüklerini silebilirsin.");
        Append(modal.Footer, Act("Kapat", modal.Close, "PrimaryButton"));
        refreshers.Add(Render);
        modal.Closed = () => refreshers.Remove(Render);
        Render();

        void AddType()
        {
            var title = name.Text.Trim();
            if (title.Length == 0) throw new InvalidOperationException("Varyasyon tipinin adını yaz.");
            Unique(title, null);
            Save("variation", new VariationType(Guid.NewGuid().ToString("N")[..12], title, []));
            name.Clear();
            Toast($"\"{title}\" eklendi.");
        }

        void Unique(string title, string? self)
        {
            if (variations.Values.Any(v => v.Id != self && string.Compare(v.Name, title, culture, System.Globalization.CompareOptions.IgnoreCase) == 0))
                throw new InvalidOperationException("Bu adla bir varyasyon tipi zaten var.");
        }

        void Render()
        {
            list.Children.Clear();
            if (variations.Count == 0) list.Children.Add(Label("Tanımlı varyasyon yok.", 13, Muted));
            foreach (var variation in variations.Values.OrderBy(v => v.Name, StringComparer.Create(culture, true)))
                list.Children.Add(TypeCard(variation));
        }

        UIElement TypeCard(VariationType variation)
        {
            var stack = new StackPanel();
            var head = new DockPanel();
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var used = products.Values.Count(p => p.Variation.Equals(variation.Id, StringComparison.OrdinalIgnoreCase));
            var rename = Act("", () => Rename(variation), "GhostButton", "");
            rename.Width = 34;
            rename.ToolTip = "Yeniden adlandır";
            rename.Foreground = Paint("#2563EB");
            System.Windows.Automation.AutomationProperties.SetName(rename, $"{variation.Name} düzenle");
            actions.Children.Add(rename);
            var delete = Act("", () => Confirm("Varyasyon tipini sil", $"\"{variation.Name}\" silinecek." +
                (used > 0 ? $" Bu tipi kullanan {used} ürün \"Varyasyon yok\" olarak satılır." : ""), "Sil", () =>
            {
                Save("variation", variation with { Archived = true });
                Toast("Varyasyon tipi silindi.");
            }), "GhostButton", "");
            delete.Width = 34;
            delete.ToolTip = "Sil";
            delete.Foreground = Paint("#DC2626");
            System.Windows.Automation.AutomationProperties.SetName(delete, $"{variation.Name} sil");
            actions.Children.Add(delete);
            var title = Hstack(Label(variation.Name, 15, bold: true), Label($"{variation.Values.Length} varyant · {used} ürün", 12, Muted));
            ((TextBlock)title.Children[1]).VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);
            stack.Children.Add(head);

            var chips = new WrapPanel { Margin = new Thickness(0, SmallGap, 0, 0) };
            foreach (var value in variation.Values)
            {
                var remove = new Button
                {
                    Style = (Style)FindResource("GhostButton"), Width = 22, Height = 22, MinHeight = 22, Padding = new Thickness(0),
                    Content = Glyph("", 9, "#DC2626"), ToolTip = "Varyantı sil", Margin = new Thickness(4, 0, 0, 0)
                };
                System.Windows.Automation.AutomationProperties.SetName(remove, $"{value} sil");
                remove.Click += (_, _) => Run(() => Save("variation", variation with { Values = variation.Values.Where(v => v != value).ToArray() }));
                var chip = new Border
                {
                    Background = Paint("#EFF4FF"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 4, 3),
                    Margin = new Thickness(0, 0, SmallGap, SmallGap), Child = Hstack(Label(value, 13, "#1D4ED8", true), remove)
                };
                ((StackPanel)chip.Child).Children[1].SetValue(MarginProperty, new Thickness(4, 0, 0, 0));
                chips.Children.Add(chip);
            }
            stack.Children.Add(chips);
            var input = Text();
            input.MaxLength = 60;
            void AddValue()
            {
                var values = input.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (values.Length == 0) throw new InvalidOperationException("Varyant adını yaz.");
                var merged = variation.Values.ToList();
                foreach (var value in values)
                    if (!merged.Contains(value, StringComparer.Create(culture, true))) merged.Add(value);
                if (merged.Count > 200) throw new InvalidOperationException("Bir tipte en fazla 200 varyant olabilir.");
                Save("variation", variation with { Values = merged.ToArray() });
            }
            input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(AddValue); } };
            stack.Children.Add(InputRow(AddValue, $"{variation.Name} varyant ekle",
                (Hinted(input, "Yeni varyant (virgülle birden fazla: Mor, Turuncu)"), new GridLength(1, GridUnitType.Star))));
            var card = Card(stack, new Thickness(Gap));
            card.Margin = new Thickness(0, 0, 0, SmallGap);
            return card;
        }

        void Rename(VariationType variation)
        {
            var field = Text(variation.Name);
            field.MaxLength = 60;
            var dialog = ShowModal("Varyasyon tipini yeniden adlandır", Field("Ad", field), 420);
            Append(dialog.Footer, Act("Vazgeç", dialog.Close));
            Append(dialog.Footer, Act("Kaydet", () =>
            {
                var title = field.Text.Trim();
                if (title.Length == 0) throw new InvalidOperationException("Adı yaz.");
                Unique(title, variation.Id);
                Save("variation", variation with { Name = title });
                dialog.Close();
            }, "PrimaryButton", ""));
            field.SelectAll();
        }
    }

    /// <summary>Satışta varyant seçimi. Seçilen varyant onSelected ile döner.</summary>
    private void PickVariant(Product product, Action<string> onSelected)
    {
        var options = OptionsOf(product);
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -SmallGap, 0) };
        Modal? modal = null;
        foreach (var option in options)
        {
            var button = new Button
            {
                Content = option, MinHeight = 56, FontSize = 15, Margin = new Thickness(0, 0, SmallGap, SmallGap)
            };
            button.Click += (_, _) => { modal!.Close(); Run(() => onSelected(option)); };
            grid.Children.Add(button);
        }
        modal = ShowModal($"{VariationName(product)} seç", grid, 520, subtitle: product.Name);
        Append(modal.Footer, Act("Vazgeç", modal.Close));
    }
}
