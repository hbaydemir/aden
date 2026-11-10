using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Threading;

namespace Aden;

public sealed partial class MainWindow : Window
{
    private readonly Store store;
    private readonly TextBlock status = new() { Margin = new Thickness(18, 8, 18, 8) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly CancellationTokenSource closing = new();
    private readonly TabControl tabs = new();
    private readonly Dictionary<string, Product> products = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Account> accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Warehouse, long Shop)> stock = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<Line> basket = new();
    private readonly ObservableCollection<Payment> payments = new();
    private readonly ObservableCollection<Line> quoteLines = new();
    private readonly List<(DataGrid Grid, Func<object[]> Data)> views = new();
    private readonly TextBlock total = new() { Margin = new Thickness(4) };
    private readonly CultureInfo culture = CultureInfo.GetCultureInfo("tr-TR");
    private bool syncing;

    public MainWindow(Store store)
    {
        this.store = store;
        Title = "Aden | Ön Muhasebe, Stok ve Satış";
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("tr-TR");
        AddProducts();
        AddStock();
        AddSales();
        AddQuotes();
        AddAccounts();
        AddCash();
        AddDocuments();
        AddReports();
        AddSettings();
        BuildWorkspace();
        Refresh();
        Totals();
        status.Text = store.Setting("mode") == "sync" ? "Eşitleme açılıyor" : "Yerel mod";
        tabs.SelectedIndex = 2;
        timer.Tick += async (_, _) => await Sync();
        timer.Start();
        Loaded += async (_, _) => { saleSearch?.Focus(); await Sync(); };
        Closed += (_, _) => { timer.Stop(); closing.Cancel(); store.Dispose(); };
    }

    private StackPanel Tab(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(3, 0, 12, 12) };
        tabs.Items.Add(new TabItem { Header = title, Content = TouchScroll(panel) });
        return panel;
    }

    private static TextBox Input(Panel panel, string label, string initial = "", double width = 135)
    {
        var field = new StackPanel { Margin = new Thickness(4) };
        var caption = Label(label, 12, "#536580", true);
        caption.Margin = new Thickness(2, 0, 0, 5);
        field.Children.Add(caption);
        var input = new TextBox { Width = width, Text = initial };
        System.Windows.Automation.AutomationProperties.SetName(input, label);
        field.Children.Add(input);
        panel.Children.Add(field);
        return input;
    }

    private static WrapPanel Form(Panel panel)
    {
        var row = new WrapPanel();
        panel.Children.Add(row);
        return row;
    }

    private static Button Button(Panel panel, string label, Action action)
    {
        var button = new Button { Content = label, Margin = new Thickness(4) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception error) { MessageBox.Show(error.Message, "Aden", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        panel.Children.Add(button);
        return button;
    }

    private static DataGrid Grid(Panel panel, string heading, double height = 195)
    {
        var title = Label(heading, 17, bold: true);
        title.Margin = new Thickness(4, 16, 4, 8);
        panel.Children.Add(title);
        var grid = new DataGrid
        {
            Height = height, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single,
            AutoGenerateColumns = true, ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
        grid.AutoGeneratingColumn += (_, e) =>
        {
            e.Column.Header = System.Text.RegularExpressions.Regex.Replace(e.PropertyName, "(?<=\\p{Ll})(?=\\p{Lu})", " ");
            if (e.PropertyType == typeof(DateTime) && e.Column is DataGridTextColumn column &&
                column.Binding is System.Windows.Data.Binding binding)
                binding.StringFormat = "dd.MM.yyyy HH:mm";
        };
        panel.Children.Add(grid);
        return grid;
    }

    private void View(Panel panel, string heading, Func<object[]> data, double height = 300) =>
        views.Add((Grid(panel, heading, height), data));

    private void AddProducts()
    {
        var panel = Tab("Ürünler");
        var row = Form(panel);
        var code = Input(row, "Ürün kodu");
        var name = Input(row, "Ürün adı", width: 200);
        var barcode = Input(row, "Barkod");
        var price = Input(row, "Satış fiyatı (TL)", "0,00");
        var minimum = Input(row, "Raf alt sınırı", "0");
        Button(panel, "Ürünü kaydet / güncelle", () =>
        {
            var id = Required(code).ToUpperInvariant();
            var bar = barcode.Text.Trim();
            if (bar.Length > 0 && products.Values.Any(p => p.Code != id && p.Barcode == bar))
                throw new InvalidOperationException("Barkod başka bir üründe kullanılıyor.");
            Save("product", new Product(id, Required(name), bar, Money(price, true), Quantity(minimum, true)));
        });
        View(panel, "Ürün kartları", () => products.Values.OrderBy(p => p.Name).Select(p => new
        {
            Kod = p.Code, Ürün = p.Name, Barkod = p.Barcode, Satış = Format(p.Price), RafAltSınırı = p.Minimum
        }).ToArray());
    }

    private void AddStock()
    {
        var panel = Tab("Depo ve stok");
        var row = Form(panel);
        var code = Input(row, "Ürün kodu");
        var count = Input(row, "Adet", "1");
        var cost = Input(row, "Alış fiyatı (TL)", "0,00");
        var reference = Input(row, "Belge / tedarikçi", width: 190);
        Button(row, "Mal kabul", () =>
        {
            var product = Find(Required(code));
            Save("stock", new Stock(product.Code, Quantity(count), 0, Money(cost, true), Required(reference)));
        });
        Button(row, "Dükkâna aktar", () =>
        {
            var product = Find(Required(code));
            var amount = Quantity(count);
            if (stock.GetValueOrDefault(product.Code).Warehouse < amount)
                throw new InvalidOperationException("Depo stoku yetersiz.");
            Save("stock", new Stock(product.Code, -amount, amount, 0, reference.Text.Trim()));
        });
        View(panel, "Mevcut stok", () => products.Values.OrderBy(p => p.Name).Select(p =>
        {
            var (warehouse, shop) = stock.GetValueOrDefault(p.Code);
            return new { Kod = p.Code, Ürün = p.Name, Depo = warehouse, Dükkân = shop, Toplam = warehouse + shop,
                RafYenileme = Math.Min(Math.Max(warehouse, 0), Math.Max(p.Minimum - shop, 0)) };
        }).ToArray(), 245);
        View(panel, "Stok hareketleri", () => Read<Stock>("stock").Reverse().Select(m => new
        {
            Tarih = m.Entry.CreatedAt.LocalDateTime, Kod = m.Value.Code, Depo = m.Value.Warehouse,
            Dükkân = m.Value.Shop, BirimAlış = Format(m.Value.UnitCost), Belge = m.Value.Reference
        }).ToArray(), 185);
    }

    private void AddSales()
    {
        var panel = CheckoutLayout();
        var cart = Grid(panel, "Ürünler", 276);
        Columns(cart, ("Ürün", "Name", 1.4), ("Adet", "Quantity", 0.6), ("Birim", "UnitPrice", 1));
        cart.ItemsSource = basket;
        var quantity = Form(panel);
        void ChangeQuantity(int delta)
        {
            if (payments.Count > 0)
                throw new InvalidOperationException("Sepeti değiştirmek için önce ödeme parçalarını kaldır.");
            if (cart.SelectedItem is not Line line) throw new InvalidOperationException("Sepetten bir ürün seç.");
            if (delta == 0 || line.Quantity + delta <= 0) basket.Remove(line);
            else if (line.Quantity + delta <= 1_000_000)
            {
                var updated = line with { Quantity = line.Quantity + delta };
                basket[basket.IndexOf(line)] = updated;
                cart.SelectedItem = updated;
            }
            Totals();
        }
        Button(quantity, "− Adet", () => ChangeQuantity(-1));
        Button(quantity, "+ Adet", () => ChangeQuantity(1));
        Button(quantity, "Çıkar", () => ChangeQuantity(0));
        var account = Input(Form(panel), "Cari kodu (isteğe bağlı)", width: 170);
        var split = new StackPanel { Margin = new Thickness(16) };
        Window? splitWindow = null;
        var row = Form(split);
        var amount = Input(row, "Ödeme parçası (TL)", width: 155);
        var methods = Form(split);
        Button(methods, "Nakit ekle", () => Pay("Nakit"));
        Button(methods, "Kart ekle", () => Pay("Kart"));

        void Pay(string method)
        {
            var value = Money(amount);
            if (value > Sum(basket) - payments.Sum(p => p.Amount))
                throw new InvalidOperationException("Ödeme, kalan tutarı aşıyor.");
            payments.Add(new(method, value, ""));
            amount.Clear();
            Totals();
        }

        var paymentGrid = Grid(split, "Ödeme parçaları", 132);
        Columns(paymentGrid, ("Tür", "Method", 1), ("Tutar", "Amount", 1));
        paymentGrid.ItemsSource = payments;
        Button(split, "Seçili ödemeyi çıkar", () =>
        {
            if (paymentGrid.SelectedItem is Payment payment) payments.Remove(payment);
            Totals();
        });

        total.FontSize = 18;
        total.FontWeight = FontWeights.SemiBold;
        total.TextWrapping = TextWrapping.Wrap;
        checkoutFooter.Children.Add(new Border { Child = total, Background = Paint("#E9EFFF"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8) });
        var actions = Form(checkoutFooter);
        Button(actions, "Nakit", () => CompleteSingle("Nakit")).Style = (Style)FindResource("PrimaryButton");
        Button(actions, "Kredi kartı", () => CompleteSingle("Kart")).Style = (Style)FindResource("PrimaryButton");
        Button(actions, "Vadeli", () => CompleteSingle("Vadeli"));
        var splitBalance = Label("", 18, bold: true);
        split.Children.Add(splitBalance);
        void UpdateSplitBalance() =>
            splitBalance.Text = $"Toplam {Format(Sum(basket))}  ·  Kalan {Format(Sum(basket) - payments.Sum(p => p.Amount))}";
        payments.CollectionChanged += (_, _) => UpdateSplitBalance();
        Button(split, "Parçalı satışı tamamla", () => CompleteSale(payments.ToArray())).Style = (Style)FindResource("PrimaryButton");
        Button(checkoutFooter, "Parçalı ödeme", () =>
        {
            if (basket.Count == 0) throw new InvalidOperationException("Sepete ürün ekle.");
            UpdateSplitBalance();
            var scroll = TouchScroll(split);
            var dialog = new Window
            {
                Title = "Aden | Parçalı ödeme", Owner = this, Icon = Icon,
                Width = 490, Height = 570, MinWidth = 400, MinHeight = 440,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false, Background = Background, Content = scroll
            };
            splitWindow = dialog;
            dialog.Loaded += (_, _) => amount.Focus();
            try { dialog.ShowDialog(); }
            finally
            {
                scroll.Content = null;
                splitWindow = null;
                saleSearch?.Focus();
            }
        });

        void CompleteSingle(string method)
        {
            if (payments.Count > 0)
                throw new InvalidOperationException("Ödeme parçaları var. Parçalı satışı tamamla veya parçaları kaldır.");
            var value = Sum(basket);
            if (value <= 0 && method == "Vadeli")
                throw new InvalidOperationException("Vadeli satış için pozitif bir sepet toplamı gerekiyor.");
            CompleteSale(value == 0 ? [] : [new Payment(method, value, "")]);
        }

        void CompleteSale(Payment[] tender)
        {
            if (basket.Count == 0 || tender.Sum(p => p.Amount) != Sum(basket))
                throw new InvalidOperationException("Sepet ve ödeme toplamını kontrol et.");
            foreach (var group in basket.GroupBy(line => line.Code))
                if (stock.GetValueOrDefault(group.Key).Shop < group.Sum(line => line.Quantity))
                    throw new InvalidOperationException($"Dükkân stoku yetersiz: {group.Key}");
            var id = account.Text.Trim().ToUpperInvariant();
            if (tender.Any(p => p.Method == "Vadeli") && id.Length == 0)
                throw new InvalidOperationException("Vadeli satış için cari kodu gir.");
            if (id.Length > 0 && !accounts.ContainsKey(id))
                throw new InvalidOperationException("Girilen cari kodu bulunamadı.");
            Save("sale", new Sale(id, basket.ToArray(), tender));
            basket.Clear();
            payments.Clear();
            account.Clear();
            amount.Clear();
            Totals();
            splitWindow?.Close();
            saleSearch?.Focus();
        }
        var history = Tab("Satış geçmişi");
        var sales = Grid(history, "Tamamlanan satışlar", 430);
        views.Add((sales, () => Read<Sale>("sale").Reverse().Select(s => new SaleRow(s.Entry.Id, s.Entry.CreatedAt.LocalDateTime, s.Value.Account,
            string.Join(", ", s.Value.Lines.Select(l => $"{l.Name} × {l.Quantity}")),
            Format(Sum(s.Value.Lines)),
            string.Join(", ", s.Value.Payments.Select(p => $"{p.Method} {Format(p.Amount)} {p.Reference}")))).ToArray()));
        Button(history, "Satış özetini yazdır", () =>
        {
            if (sales.SelectedItem is not SaleRow selected) throw new InvalidOperationException("Satış seç.");
            var saleId = selected.Satış;
            var sale = Read<Sale>("sale").Single(s => s.Entry.Id == saleId);
            var document = new FlowDocument { PagePadding = new Thickness(45), FontFamily = FontFamily };
            document.Blocks.Add(new Paragraph(new Bold(new Run(store.Setting("company", "Aden")))) { FontSize = 24 });
            document.Blocks.Add(new Paragraph(new Run($"SATIŞ ÖZETİ · Mali belge yerine geçmez\n{sale.Entry.CreatedAt.LocalDateTime:g}\n{saleId}")));
            foreach (var line in sale.Value.Lines)
                document.Blocks.Add(new Paragraph(new Run($"{line.Name} × {line.Quantity}   {Format(line.UnitPrice * line.Quantity)}")));
            document.Blocks.Add(new Paragraph(new Bold(new Run($"Toplam {Format(Sum(sale.Value.Lines))}"))));
            foreach (var payment in sale.Value.Payments)
                document.Blocks.Add(new Paragraph(new Run($"{payment.Method}   {Format(payment.Amount)}   {payment.Reference}")));
            Print(document, "Aden satış özeti");
        });
    }

    private record SaleRow(Guid Satış, DateTime Tarih, string Cari, string Ürünler, string Toplam, string Ödemeler);

    private static void Print(FlowDocument document, string title)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PageHeight = dialog.PrintableAreaHeight;
        document.ColumnWidth = document.PageWidth;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, title);
    }

    private void ProductPicker(Panel panel, ObservableCollection<Line> target)
    {
        var row = Form(panel);
        var search = Input(row, "Barkod / kod / ürün adı", width: 220);
        var count = Input(row, "Adet", "1", 70);
        var list = new ComboBox { Width = 230, Margin = new Thickness(4), DisplayMemberPath = nameof(Product.Name) };
        row.Children.Add(list);
        views.Add((new DataGrid(), () =>
        {
            var selected = (list.SelectedItem as Product)?.Code;
            list.ItemsSource = products.Values.OrderBy(p => p.Name).ToArray();
            list.SelectedItem = list.Items.Cast<Product>().FirstOrDefault(p => p.Code == selected);
            return Array.Empty<object>();
        }));
        void Add()
        {
            var product = search.Text.Trim().Length > 0 ? Find(Required(search)) :
                list.SelectedItem as Product ?? throw new InvalidOperationException("Ürün seç.");
            target.Add(new(product.Code, product.Name, Quantity(count), product.Price));
            search.Clear();
            list.SelectedIndex = -1;
            Totals();
            search.Focus();
        }
        Button(row, "Ürün ekle", Add);
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            try { Add(); }
            catch (Exception error) { MessageBox.Show(error.Message, "Aden"); }
        };
    }

    private void AddQuotes()
    {
        var panel = Tab("Teklifler");
        var row = Form(panel);
        var customer = Input(row, "Müşteri / unvan", width: 220);
        var contact = Input(row, "İletişim / adres", width: 220);
        ProductPicker(panel, quoteLines);
        var lines = Grid(panel, "Teklif kalemleri", 150);
        lines.ItemsSource = quoteLines;
        var list = Grid(panel, "Kayıtlı teklifler", 200);
        views.Add((list, () => Read<Quote>("quote").Reverse().Select(q => new QuoteRow(q.Entry.Id,
            q.Value.Customer, q.Entry.CreatedAt.LocalDateTime, Format(Sum(q.Value.Lines)))).ToArray()));
        var buttons = Form(panel);
        Button(buttons, "Kalemi çıkar", () => { if (lines.SelectedItem is Line line) quoteLines.Remove(line); });
        Button(buttons, "Teklifi kaydet", () =>
        {
            if (quoteLines.Count == 0) throw new InvalidOperationException("En az bir ürün ekle.");
            Save("quote", new Quote(Required(customer), contact.Text.Trim(), quoteLines.ToArray()));
            quoteLines.Clear();
        });
        Button(buttons, "Teklifi satış sepetine aktar", () =>
        {
            if (list.SelectedItem is not QuoteRow selected) throw new InvalidOperationException("Teklif seç.");
            if (basket.Count > 0 || payments.Count > 0)
                throw new InvalidOperationException("Aktarmadan önce mevcut satış sepetini tamamla veya boşalt.");
            var quote = Read<Quote>("quote").Single(q => q.Entry.Id == selected.No).Value;
            foreach (var line in quote.Lines) basket.Add(line);
            Totals();
            tabs.SelectedIndex = 2;
        });
        var export = Button(buttons, "PDF olarak dışa aktar", () =>
        {
            if (list.SelectedItem is not QuoteRow selected) throw new InvalidOperationException("Teklif seç.");
            var quote = Read<Quote>("quote").Single(q => q.Entry.Id == selected.No).Value;
            var dialog = new SaveFileDialog
            {
                Title = "Teklifi PDF olarak kaydet", Filter = "PDF dosyası (*.pdf)|*.pdf",
                DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = true,
                FileName = $"Aden-Teklif-{selected.No.ToString("N")[..12]}"
            };
            if (dialog.ShowDialog(this) != true) return;
            QuotePdf.Save(dialog.FileName, selected.No, selected.Tarih, quote);
            status.Text = $"PDF kaydedildi · {System.IO.Path.GetFileName(dialog.FileName)}";
        });
        export.Style = (Style)FindResource("PrimaryButton");
        Button(buttons, "Seçili teklifi yazdır", () =>
        {
            if (list.SelectedItem is not QuoteRow selected) throw new InvalidOperationException("Teklif seç.");
            var quote = Read<Quote>("quote").Single(q => q.Entry.Id == selected.No).Value;
            var document = new FlowDocument { PagePadding = new Thickness(45), FontFamily = FontFamily };
            var logo = store.Setting("quoteLogo");
            if (logo.Length > 0)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new System.IO.MemoryStream(Convert.FromBase64String(logo));
                bitmap.DecodePixelWidth = 300;
                bitmap.EndInit();
                document.Blocks.Add(new BlockUIContainer(new Image
                {
                    Source = bitmap, Width = 145, MaxHeight = 75,
                    Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left
                }));
            }
            document.Blocks.Add(new Paragraph(new Bold(new Run(store.Setting("company", "Aden")))) { FontSize = 25 });
            document.Blocks.Add(new Paragraph(new Run(store.Setting("companyContact"))));
            document.Blocks.Add(new Paragraph(new Bold(new Run("SATIŞ TEKLİFİ"))));
            document.Blocks.Add(new Paragraph(new Run($"{selected.Tarih:d}   •   {selected.No}\n{quote.Customer}\n{quote.Contact}")));
            foreach (var line in quote.Lines)
                document.Blocks.Add(new Paragraph(new Run($"{line.Name}   × {line.Quantity}   •   {Format(line.UnitPrice * line.Quantity)}")));
            document.Blocks.Add(new Paragraph(new Bold(new Run($"Toplam {selected.Toplam}"))));
            Print(document, "Aden satış teklifi");
        });
    }

    private record QuoteRow(Guid No, string Müşteri, DateTime Tarih, string Toplam);

    private void AddAccounts()
    {
        var panel = Tab("Cariler");
        var row = Form(panel);
        var code = Input(row, "Cari kodu");
        var name = Input(row, "Ad / unvan", width: 200);
        var contact = Input(row, "İletişim", width: 200);
        Button(row, "Cariyi kaydet / güncelle", () =>
            Save("account", new Account(Required(code).ToUpperInvariant(), Required(name), contact.Text.Trim())));
        View(panel, "Cari bakiyeler", () => accounts.Values.Select(a => new
        {
            Kod = a.Code, Cari = a.Name, İletişim = a.Contact,
            Bakiye = Format(Read<Sale>("sale").Where(s => s.Value.Account == a.Code)
                .Sum(s => s.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount))
                - Read<Cash>("cash").Where(c => c.Value.Account == a.Code).Sum(c => c.Value.Amount))
        }).ToArray());
        View(panel, "Cari hareketler", () =>
            Read<Sale>("sale").Where(s => s.Value.Payments.Any(p => p.Method == "Vadeli"))
                .Select(s => new { Tarih = s.Entry.CreatedAt.LocalDateTime, Cari = s.Value.Account,
                    İşlem = "Vadeli satış", Tutar = Format(s.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount)) })
                .Concat(Read<Cash>("cash").Where(c => c.Value.Account.Length > 0).Select(c =>
                    new { Tarih = c.Entry.CreatedAt.LocalDateTime, Cari = c.Value.Account,
                        İşlem = c.Value.Category, Tutar = Format(c.Value.Amount) }))
                .OrderByDescending(item => item.Tarih).ToArray(), 185);
    }

    private void AddCash()
    {
        var panel = Tab("Kasa ve gelir gider");
        var row = Form(panel);
        var detail = Input(row, "Kategori / açıklama", width: 230);
        var amount = Input(row, "Tutar (TL)");
        var account = Input(row, "Cari kodu");
        Button(row, "Gelir", () => Record("Gelir", 1, false));
        Button(row, "Gider", () => Record("Gider", -1, false));
        Button(row, "Tahsilat", () => Record("Tahsilat", 1, true));
        Button(row, "Cari ödeme", () => Record("Cari ödeme", -1, true));
        void Record(string kind, int sign, bool useAccount)
        {
            var id = useAccount ? Required(account).ToUpperInvariant() : "";
            if (useAccount && !accounts.ContainsKey(id))
                throw new InvalidOperationException("Geçerli cari kodu gir.");
            Save("cash", new Cash(kind, Required(detail), id, sign * Money(amount)));
        }
        var balance = new TextBlock { Margin = new Thickness(4, 12, 4, 4) };
        panel.Children.Add(balance);
        View(panel, "Kasa hareketleri", () =>
        {
            var sales = Read<Sale>("sale").Select(s => new
            {
                Tarih = s.Entry.CreatedAt.LocalDateTime, İşlem = "Nakit satış", Açıklama = s.Entry.Id.ToString(),
                Tutar = s.Value.Payments.Where(p => p.Method == "Nakit").Sum(p => p.Amount)
            }).Where(s => s.Tutar != 0);
            var cash = Read<Cash>("cash").Select(c => new
            {
                Tarih = c.Entry.CreatedAt.LocalDateTime, İşlem = c.Value.Category, Açıklama = c.Value.Detail, Tutar = c.Value.Amount
            });
            var all = sales.Concat(cash).OrderByDescending(c => c.Tarih).ToArray();
            balance.Text = "Kasa bakiyesi: " + Format(all.Sum(c => c.Tutar));
            return all.Select(c => new { c.Tarih, c.İşlem, c.Açıklama, Tutar = Format(c.Tutar) }).ToArray();
        });
    }

    private void AddReports()
    {
        var panel = Tab("Raporlar");
        var row = Form(panel);
        var start = new DatePicker { SelectedDate = DateTime.Today.AddDays(1 - DateTime.Today.Day), Margin = new Thickness(4) };
        var end = new DatePicker { SelectedDate = DateTime.Today, Margin = new Thickness(4) };
        row.Children.Add(new TextBlock { Text = "Başlangıç", Margin = new Thickness(4) });
        row.Children.Add(start);
        row.Children.Add(new TextBlock { Text = "Bitiş", Margin = new Thickness(4) });
        row.Children.Add(end);
        var grid = Grid(panel, "Dönem özeti", 280);
        object[] Report()
        {
            if (start.SelectedDate is not DateTime from || end.SelectedDate is not DateTime to || to < from)
                return Array.Empty<object>();
            bool InRange(Entry entry) => entry.CreatedAt.LocalDateTime.Date >= from && entry.CreatedAt.LocalDateTime.Date <= to;
            var sales = Read<Sale>("sale").Where(sale => InRange(sale.Entry)).Select(sale => sale.Value).ToArray();
            var tenders = sales.SelectMany(sale => sale.Payments).ToArray();
            var cash = Read<Cash>("cash").Where(movement => InRange(movement.Entry)).Select(movement => movement.Value).ToArray();
            return new[]
            {
                new { Kalem = "Toplam satış", Tutar = Format(sales.Sum(sale => Sum(sale.Lines))) },
                new { Kalem = "Nakit satış", Tutar = Format(tenders.Where(payment => payment.Method == "Nakit").Sum(payment => payment.Amount)) },
                new { Kalem = "Kart satış", Tutar = Format(tenders.Where(payment => payment.Method == "Kart").Sum(payment => payment.Amount)) },
                new { Kalem = "Vadeli satış", Tutar = Format(tenders.Where(payment => payment.Method == "Vadeli").Sum(payment => payment.Amount)) },
                new { Kalem = "Diğer gelir", Tutar = Format(cash.Where(movement => movement.Category == "Gelir").Sum(movement => movement.Amount)) },
                new { Kalem = "Gider", Tutar = Format(-cash.Where(movement => movement.Category == "Gider").Sum(movement => movement.Amount)) }
            };
        }
        views.Add((grid, Report));
        start.SelectedDateChanged += (_, _) => grid.ItemsSource = Report();
        end.SelectedDateChanged += (_, _) => grid.ItemsSource = Report();
    }

    private void AddDocuments()
    {
        var panel = Tab("Entegrasyonlar");
        panel.Children.Add(new TextBlock
        {
            Text = "E-Fatura ve ÖKC sağlayıcısı henüz yapılandırılmadı. Kart kayıtları manuel teyit gerektirir.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4)
        });
    }

    private void AddSettings()
    {
        var panel = Tab("Ayarlar");
        AddUpdateSettings(panel);
        var profile = Form(panel);
        var company = Input(profile, "Firma unvanı", store.Setting("company", "Aden"), 240);
        var contact = Input(profile, "Antet iletişim / adres", store.Setting("companyContact"), 300);
        Button(profile, "Firma bilgilerini kaydet", () =>
        {
            store.SaveSetting("company", Required(company));
            store.SaveSetting("companyContact", contact.Text.Trim());
        });
        Button(profile, "Teklif logosu seç", () =>
        {
            var picker = new OpenFileDialog { Filter = "Resim|*.png;*.jpg;*.jpeg" };
            if (picker.ShowDialog(this) != true) return;
            var file = new System.IO.FileInfo(picker.FileName);
            if (file.Length > 2_000_000) throw new InvalidOperationException("Logo en fazla 2 MB olabilir.");
            var bytes = System.IO.File.ReadAllBytes(picker.FileName);
            using var image = new System.IO.MemoryStream(bytes);
            BitmapDecoder.Create(image, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            store.SaveSetting("quoteLogo", Convert.ToBase64String(bytes));
        });
        Button(profile, "Logoyu kaldır", () => store.SaveSetting("quoteLogo", ""));
        var enabled = new CheckBox
        {
            Content = "Sunucu eşitlemesini aç", IsChecked = store.Setting("mode") == "sync", Margin = new Thickness(4)
        };
        panel.Children.Add(enabled);
        var row = Form(panel);
        var url = Input(row, "Sunucu adresi", store.Setting("url", "http://127.0.0.1:5080"), 300);
        var password = new PasswordBox { Password = store.Setting("key"), Width = 240, Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = "Eşitleme anahtarı", Margin = new Thickness(4) });
        panel.Children.Add(password);
        Button(panel, "Çalışma modunu kaydet", () =>
        {
            if (syncing) throw new InvalidOperationException("Eşitleme tamamlanınca tekrar dene.");
            var endpoint = url.Text.Trim().TrimEnd('/');
            if (enabled.IsChecked == true)
            {
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                    !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ||
                    uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 || password.Password.Length < 24)
                    throw new InvalidOperationException("HTTPS adresi veya yerel HTTP adresi ve en az 24 karakter anahtar gir.");
                store.SaveSyncSettings("sync", endpoint, password.Password);
                status.Text = "Eşitleme bekleniyor";
            }
            else
            {
                store.SaveSyncSettings("local", "", "");
                status.Text = "Yerel mod";
            }
        });
    }

    private async Task Sync()
    {
        if (syncing || closing.IsCancellationRequested || store.Setting("mode") != "sync") return;
        syncing = true;
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(store.Setting("url") + "/"), Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Add("X-Aden-Key", store.Setting("key"));
            var pending = store.Read(true);
            if (pending.Count > 0)
            {
                using var result = await http.PostAsJsonAsync("sync/push", pending, closing.Token);
                closing.Token.ThrowIfCancellationRequested();
                result.EnsureSuccessStatusCode();
                store.Acknowledge(pending);
            }
            var cursor = long.Parse(store.Setting("cursor", "0"), CultureInfo.InvariantCulture);
            var page = await http.GetFromJsonAsync<SyncPage>($"sync/pull?after={cursor}", closing.Token)
                ?? throw new InvalidOperationException("Sunucu boş eşitleme yanıtı döndürdü.");
            closing.Token.ThrowIfCancellationRequested();
            if (page.Changes.Length > 0)
            {
                store.Import(page);
                Refresh();
            }
            status.Text = $"Sunucu bağlı · Son eşitleme {DateTime.Now:HH:mm:ss}" +
                (store.Read(true).Count > 0 ? " · Bekleyen kayıtlar var" : "");
        }
        catch (Exception error) when (!closing.IsCancellationRequested)
        {
            status.Text = $"Yerel çalışma devam ediyor · Eşitleme: {error.Message}";
        }
        catch (OperationCanceledException) when (closing.IsCancellationRequested) { }
        finally { syncing = false; }
    }

    private void Save<T>(string kind, T data)
    {
        store.Add(kind, data);
        Refresh();
        if (store.Setting("mode") == "sync") _ = Sync();
    }

    private Entry[] snapshot = Array.Empty<Entry>();
    private IEnumerable<(Entry Entry, T Value)> Read<T>(string kind) =>
        snapshot.Where(entry => entry.Kind == kind)
            .Select(entry => (entry, JsonSerializer.Deserialize<T>(entry.Data)!));

    private void Refresh()
    {
        snapshot = store.Read().ToArray();
        products.Clear();
        accounts.Clear();
        stock.Clear();
        foreach (var item in Read<Product>("product")) products[item.Value.Code] = item.Value;
        foreach (var item in Read<Account>("account")) accounts[item.Value.Code] = item.Value;
        foreach (var item in Read<Stock>("stock")) Move(item.Value.Code, item.Value.Warehouse, item.Value.Shop);
        foreach (var sale in Read<Sale>("sale"))
            foreach (var line in sale.Value.Lines) Move(line.Code, 0, -line.Quantity);
        foreach (var view in views) view.Grid.ItemsSource = view.Data();
        refreshCatalog?.Invoke();
    }

    private void Move(string code, int warehouse, int shop)
    {
        var old = stock.GetValueOrDefault(code);
        stock[code] = (old.Warehouse + warehouse, old.Shop + shop);
    }

    private Product Find(string value)
    {
        if (products.TryGetValue(value, out var product)) return product;
        var barcode = products.Values.FirstOrDefault(p => p.Barcode == value);
        if (barcode is not null) return barcode;
        var matches = products.Values.Where(p =>
            culture.CompareInfo.IndexOf(p.Name, value, CompareOptions.IgnoreCase) >= 0).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException(
            matches.Length == 0 ? "Ürün bulunamadı." : "Birden fazla ürün bulundu. Ürün kodunu veya barkodu gir.");
    }

    private static string Required(TextBox field) => field.Text.Trim() is { Length: > 0 } value
        ? value : throw new InvalidOperationException("Zorunlu alanları doldur.");

    private static int Quantity(TextBox field, bool zero = false) =>
        int.TryParse(field.Text, out var value) && value >= (zero ? 0 : 1) && value <= 1_000_000
            ? value : throw new InvalidOperationException("Geçerli adet gir.");

    private long Money(TextBox field, bool zero = false)
    {
        if (decimal.TryParse(field.Text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite |
                NumberStyles.AllowTrailingWhite, culture, out var value) &&
            value >= (zero ? 0 : 0.01m) && value <= 100_000_000m && decimal.Truncate(value * 100) == value * 100)
            return (long)(value * 100);
        throw new InvalidOperationException("Tutarı 0,00 biçiminde gir.");
    }

    private static long Sum(IEnumerable<Line> lines) => lines.Sum(line => line.Quantity * line.UnitPrice);
    private string Format(long amount) => (amount / 100m).ToString("C2", culture);
    private void Totals() => total.Text = $"Toplam {Format(Sum(basket))}\nKalan {Format(Sum(basket) - payments.Sum(p => p.Amount))}";
}














