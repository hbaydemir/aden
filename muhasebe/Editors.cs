using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Aden;

public sealed partial class MainWindow
{
    private static TextBox Multiline(Panel panel, string label, string initial = "", double width = 350)
    {
        var field = Input(panel, label, initial, width);
        field.AcceptsReturn = true;
        field.TextWrapping = TextWrapping.Wrap;
        field.Height = 110;
        field.VerticalContentAlignment = VerticalAlignment.Top;
        ScrollViewer.SetVerticalScrollBarVisibility(field, ScrollBarVisibility.Auto);
        return field;
    }

    private bool Matches(string term, params string[] fields) => term.Length == 0 ||
        fields.Any(field => culture.CompareInfo.IndexOf(field, term, System.Globalization.CompareOptions.IgnoreCase) >= 0);

    private bool ConfirmDelete(string item) =>
        MessageBox.Show(this, $"{item} silinsin mi?", "Aden", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void AddProducts()
    {
        var panel = Tab("Ürünler");
        var row = Form(panel);
        var code = Input(row, "Ürün kodu");
        var name = Input(row, "Ürün adı", width: 200);
        var barcode = Input(row, "Barkod");
        var price = Input(row, "Satış fiyatı (TL)", "0,00");
        var minimum = Input(row, "Raf alt sınırı", "0");
        var actions = Form(panel);
        var grid = Grid(panel, "Ürün kartları", 380);
        Columns(grid, ("Kod", "Code", 1), ("Ürün", "Name", 2), ("Barkod", "Barcode", 1.5),
            ("Satış fiyatı", "Price", 1), ("Alt sınır", "Minimum", 1));
        views.Add((grid, () => products.Values.OrderBy(p => p.Name).ToArray()));
        var add = Button(actions, "Ürün ekle", () => Write(false));
        var update = Button(actions, "Güncelle", () => Write(true));
        var delete = Button(actions, "Sil", () =>
        {
            if (!products.TryGetValue(Key(), out var product) || !ConfirmDelete(product.Name)) return;
            Save("product", product with { Archived = true });
            Reset();
        });
        Button(actions, "Yeni ürün", Reset);
        code.TextChanged += (_, _) => ShowActions();
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is not Product selected) return;
            code.Text = selected.Code;
            name.Text = selected.Name;
            barcode.Text = selected.Barcode;
            price.Text = (selected.Price / 100m).ToString("F2", culture);
            minimum.Text = selected.Minimum.ToString(culture);
        };
        ShowActions();

        string Key() => code.Text.Trim().ToUpperInvariant();
        void ShowActions()
        {
            var exists = products.ContainsKey(Key());
            add.Visibility = exists ? Visibility.Collapsed : Visibility.Visible;
            update.Visibility = delete.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        }
        void Reset()
        {
            grid.SelectedItem = null;
            code.Clear();
            name.Clear();
            barcode.Clear();
            price.Text = "0,00";
            minimum.Text = "0";
            code.Focus();
        }
        void Write(bool editing)
        {
            var id = Required(code).ToUpperInvariant();
            if (products.ContainsKey(id) != editing)
                throw new InvalidOperationException("Ürün listesi değişti. Kaydı tekrar seç.");
            if (!editing && Read<Product>("product").Any(p => p.Value.Code.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Bu ürün kodu geçmiş kayıtlarda kullanılıyor.");
            var bar = barcode.Text.Trim();
            if (bar.Length > 0 && products.Values.Any(p => p.Code != id && p.Barcode == bar))
                throw new InvalidOperationException("Barkod başka bir üründe kullanılıyor.");
            Save("product", new Product(id, Required(name), bar, Money(price, true), Quantity(minimum, true)));
            Reset();
        }
    }

    private void AddAccounts()
    {
        var page = Tab("Cariler");
        var list = new StackPanel();
        var editor = new StackPanel { Visibility = Visibility.Collapsed };
        page.Children.Add(list);
        page.Children.Add(editor);
        var search = Input(Form(list), "Cari kodu, müşteri adı veya iletişim", width: 350);
        var grid = Grid(list, "Cari listesi", 320);
        Columns(grid, ("Kod", "Code", 1), ("Cari", "Name", 1.6), ("İletişim", "Contact", 1.7), ("Bakiye", "Balance", 1));
        object[] Filter() => accounts.Values.Where(a => Matches(search.Text.Trim(), a.Code, a.Name, a.Contact))
            .OrderBy(a => a.Name).Select(a => new AccountRow(a.Code, a.Name, a.Contact, Format(AccountBalance(a.Code)))).ToArray();
        views.Add((grid, Filter));
        search.TextChanged += (_, _) => grid.ItemsSource = Filter();
        View(list, "Cari hareketler", () =>
            Read<Sale>("sale").Where(s => s.Value.Payments.Any(p => p.Method == "Vadeli"))
                .Select(s => new { Tarih = s.Entry.CreatedAt.LocalDateTime, Cari = s.Value.Account,
                    İşlem = "Vadeli satış", Tutar = Format(s.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount)) })
                .Concat(Read<Cash>("cash").Where(c => c.Value.Account.Length > 0).Select(c =>
                    new { Tarih = c.Entry.CreatedAt.LocalDateTime, Cari = c.Value.Account,
                        İşlem = c.Value.Category, Tutar = Format(c.Value.Amount) }))
                .OrderByDescending(item => item.Tarih).ToArray(), 185);
        var code = Input(Form(editor), "Cari kodu", width: 200);
        var name = Input(Form(editor), "Ad / unvan", width: 350);
        var contact = Multiline(Form(editor), "Adres ve iletişim");
        Button(editor, "Kaydet", () =>
        {
            var id = Required(code).ToUpperInvariant();
            if (!code.IsReadOnly && Read<Account>("account").Any(a => a.Value.Code.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Bu cari kodu daha önce kullanılmış.");
            if (code.IsReadOnly && !accounts.ContainsKey(id))
                throw new InvalidOperationException("Cari başka bir cihazda silinmiş.");
            Save("account", new Account(id, Required(name), contact.Text.Trim()));
            CloseEditor();
        }).Style = (Style)FindResource("PrimaryButton");
        Button(editor, "Listeye dön", CloseEditor);

        var actions = Form(list);
        Button(actions, "Yeni cari", () => Open(null)).Style = (Style)FindResource("PrimaryButton");
        Button(actions, "Düzenle", () => Open(Selected()));
        Button(actions, "Sil", () =>
        {
            var account = Selected();
            if (AccountBalance(account.Code) != 0)
                throw new InvalidOperationException("Cariyi silmeden önce bakiyesini kapat.");
            if (ConfirmDelete(account.Name)) Save("account", account with { Archived = true });
        });
        Account Selected() => grid.SelectedItem is AccountRow selected && accounts.TryGetValue(selected.Code, out var item)
            ? item : throw new InvalidOperationException("Listeden bir cari seç.");
        void Open(Account? account)
        {
            list.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            code.IsReadOnly = account is not null;
            code.Text = account?.Code ?? "";
            name.Text = account?.Name ?? "";
            contact.Text = account?.Contact ?? "";
            if (account is null) code.Focus(); else name.Focus();
        }
        void CloseEditor()
        {
            editor.Visibility = Visibility.Collapsed;
            list.Visibility = Visibility.Visible;
        }
    }

    private sealed record AccountRow(string Code, string Name, string Contact, string Balance);

    private long AccountBalance(string code) =>
        Read<Sale>("sale").Where(s => s.Value.Account == code)
            .Sum(s => s.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount))
        - Read<Cash>("cash").Where(c => c.Value.Account == code).Sum(c => c.Value.Amount);

    private Dictionary<Guid, (DateTime Date, Quote Quote)> Quotes()
    {
        var quotes = Read<Quote>("quote").ToDictionary(q => q.Entry.Id, q => (q.Entry.CreatedAt.LocalDateTime, q.Value));
        foreach (var change in Read<QuoteRevision>("quote-revision"))
        {
            var id = change.Value.QuoteId;
            if (change.Value.Value is null) quotes.Remove(id);
            else if (quotes.TryGetValue(id, out var original)) quotes[id] = (original.LocalDateTime, change.Value.Value);
        }
        return quotes;
    }

    private void AddQuotes()
    {
        var page = Tab("Teklifler");
        var list = new StackPanel();
        var editor = new StackPanel { Visibility = Visibility.Collapsed };
        page.Children.Add(list);
        page.Children.Add(editor);
        var actions = Form(list);
        var grid = Grid(list, "Kayıtlı teklifler", 420);
        views.Add((grid, () => Quotes().OrderByDescending(q => q.Value.Date).Select(q => new QuoteRow(
            q.Key, q.Value.Quote.Customer, q.Value.Date, Format(Sum(q.Value.Quote.Lines)))).ToArray()));
        Guid? editing = null;
        var accountCode = "";
        var customer = Input(Form(editor), "Müşteri / unvan", width: 350);
        var contact = Multiline(Form(editor), "Müşteri adresi ve iletişim");
        var pick = Form(editor);
        var query = Input(pick, "Cari kodu veya müşteri adı", width: 220);
        var matches = new ComboBox { Width = 215, Margin = new Thickness(4), DisplayMemberPath = nameof(AccountChoice.Label) };
        pick.Children.Add(matches);
        query.TextChanged += (_, _) => UpdateMatches();
        Button(pick, "Seçili cariyi ekle", () =>
        {
            if (matches.SelectedItem is not AccountChoice choice || !accounts.TryGetValue(choice.Code, out var account))
                throw new InvalidOperationException("Bir cari seç.");
            accountCode = account.Code;
            customer.Text = account.Name;
            contact.Text = account.Contact;
        });
        Button(pick, "Manuel müşteri", () =>
        {
            accountCode = "";
            customer.Clear();
            contact.Clear();
            customer.Focus();
        });
        ProductPicker(editor, quoteLines);
        var lines = Grid(editor, "Teklif kalemleri", 210);
        Columns(lines, ("Ürün", "Name", 2), ("Adet", "Quantity", 1), ("Birim fiyat", "UnitPrice", 1));
        lines.ItemsSource = quoteLines;
        var totalLabel = Label("Genel toplam  " + Format(0), 18, bold: true);
        editor.Children.Add(totalLabel);
        quoteLines.CollectionChanged += (_, _) => totalLabel.Text = "Genel toplam  " + Format(Sum(quoteLines));
        var editingActions = Form(editor);
        Button(editingActions, "Kalemi çıkar", () => { if (lines.SelectedItem is Line line) quoteLines.Remove(line); });
        Button(editingActions, "Kaydet", () =>
        {
            if (quoteLines.Count == 0) throw new InvalidOperationException("En az bir ürün ekle.");
            var quote = new Quote(Required(customer), contact.Text.Trim(), quoteLines.ToArray(), accountCode);
            if (editing is Guid id)
            {
                if (!Quotes().ContainsKey(id)) throw new InvalidOperationException("Teklif başka bir cihazda silinmiş.");
                Save("quote-revision", new QuoteRevision(id, quote));
            }
            else Save("quote", quote);
            CloseEditor();
        }).Style = (Style)FindResource("PrimaryButton");
        Button(editingActions, "Listeye dön", () =>
        {
            if (MessageBox.Show(this, "Değişiklikleri kaydetmeden teklif listesine dönülsün mü?",
                "Aden", MessageBoxButton.YesNo) == MessageBoxResult.Yes) CloseEditor();
        });

        Button(actions, "Yeni teklif", () => Open(null)).Style = (Style)FindResource("PrimaryButton");
        Button(actions, "Düzenle", () => Open(Selected()));
        Button(actions, "Sil", () =>
        {
            var id = Selected();
            if (ConfirmDelete(Quotes()[id].Quote.Customer + " teklifi"))
                Save("quote-revision", new QuoteRevision(id, null));
        });
        Button(actions, "PDF olarak dışa aktar", () =>
        {
            var id = Selected();
            var (date, quote) = Quotes()[id];
            var dialog = new SaveFileDialog
            {
                Title = "Teklifi PDF olarak kaydet", Filter = "PDF dosyası (*.pdf)|*.pdf",
                DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = true,
                FileName = $"Aden-Teklif-{id.ToString("N")[..12]}"
            };
            if (dialog.ShowDialog(this) != true) return;
            QuotePdf.Save(dialog.FileName, id, date, quote, store.Setting("company", "Aden"),
                store.Setting("companyContact"), store.Setting("quoteLogo"));
            status.Text = $"PDF kaydedildi · {System.IO.Path.GetFileName(dialog.FileName)}";
        });
        Button(actions, "Yazdır", () =>
        {
            var (date, quote) = Quotes()[Selected()];
            var document = new FlowDocument { PagePadding = new Thickness(45), FontFamily = FontFamily };
            var logo = Brand.Wordmark;
            var configuredLogo = store.Setting("quoteLogo");
            if (configuredLogo.Length > 0)
            {
                using var stream = new System.IO.MemoryStream(Convert.FromBase64String(configuredLogo));
                var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                logo = bitmap.Frames[0];
            }
            document.Blocks.Add(new BlockUIContainer(new Image { Source = logo, Width = 145, MaxHeight = 75,
                HorizontalAlignment = HorizontalAlignment.Left }));
            document.Blocks.Add(new Paragraph(new Bold(new Run("SATIŞ TEKLİFİ"))) { FontSize = 25 });
            document.Blocks.Add(new Paragraph(new Run($"{store.Setting("company", "Aden")}\n{store.Setting("companyContact")}\n{date:d}")));
            document.Blocks.Add(new Paragraph(new Run($"{quote.Customer}\n{quote.Contact}")));
            foreach (var line in quote.Lines)
                document.Blocks.Add(new Paragraph(new Run($"{line.Name} × {line.Quantity}   {Format(line.UnitPrice * line.Quantity)}")));
            document.Blocks.Add(new Paragraph(new Bold(new Run($"Toplam {Format(Sum(quote.Lines))}"))));
            Print(document, "Aden satış teklifi");
        });
        Button(actions, "Satış sepetine aktar", () =>
        {
            if (basket.Count > 0 || payments.Count > 0)
                throw new InvalidOperationException("Önce mevcut satış sepetini tamamla veya boşalt.");
            foreach (var line in Quotes()[Selected()].Quote.Lines)
            {
                if (!products.ContainsKey(line.Code))
                    throw new InvalidOperationException($"Teklifte silinmiş ürün var: {line.Name}");
            }
            foreach (var line in Quotes()[Selected()].Quote.Lines) basket.Add(line);
            Totals();
            tabs.SelectedIndex = 2;
        });

        Guid Selected() => grid.SelectedItem is QuoteRow selected ? selected.No
            : throw new InvalidOperationException("Listeden bir teklif seç.");
        void UpdateMatches()
        {
            matches.ItemsSource = accounts.Values.Where(a => Matches(query.Text.Trim(), a.Code, a.Name))
                .OrderBy(a => a.Name).Select(a => new AccountChoice(a.Code, $"{a.Code} · {a.Name}")).ToArray();
            matches.SelectedIndex = matches.Items.Count > 0 ? 0 : -1;
        }
        void Open(Guid? id)
        {
            editing = id;
            var quote = id is Guid value ? Quotes()[value].Quote : null;
            accountCode = quote?.AccountCode ?? "";
            customer.Text = quote?.Customer ?? "";
            contact.Text = quote?.Contact ?? "";
            query.Clear();
            UpdateMatches();
            quoteLines.Clear();
            foreach (var line in quote?.Lines ?? []) quoteLines.Add(line);
            list.Visibility = Visibility.Collapsed;
            editor.Visibility = Visibility.Visible;
            customer.Focus();
        }
        void CloseEditor()
        {
            quoteLines.Clear();
            editor.Visibility = Visibility.Collapsed;
            list.Visibility = Visibility.Visible;
        }
    }

    private sealed record AccountChoice(string Code, string Label);
    private sealed record QuoteRow(Guid No, string Müşteri, DateTime Tarih, string Toplam);
}
