using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.Win32;

namespace Aden;

/// <summary>Excel (.xlsx) ve CSV dosyası okuma/yazma. Tek sayfa, ilk satır başlık.</summary>
public static class DataTransfer
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public sealed record Sheet(string Name, string[] Headers, List<object?[]> Rows);

    public static void Write(string path, Sheet sheet)
    {
        if (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)) WriteCsv(path, sheet);
        else
        {
            using var book = new XLWorkbook();
            AddSheet(book, sheet);
            book.SaveAs(path);
        }
    }

    /// <summary>Rapor tablolarını (anonim nesne listeleri) ayrı sayfalara yazar.</summary>
    public static void SaveTables(string path, (string Name, System.Collections.IEnumerable? Items)[] tables)
    {
        using var book = new XLWorkbook();
        foreach (var (name, items) in tables)
        {
            var list = items?.Cast<object>().ToList() ?? [];
            if (list.Count == 0) continue;
            var properties = list[0].GetType().GetProperties();
            var headers = properties.Select(p => System.Text.RegularExpressions.Regex.Replace(p.Name, "(?<=\\p{Ll})(?=\\p{Lu})", " ").Replace("Kdv", "KDV")).ToArray();
            var rows = list.Select(item => properties.Select(p => p.GetValue(item)).ToArray()).ToList();
            AddSheet(book, new Sheet(name, headers, rows));
        }
        if (!book.Worksheets.Any()) book.AddWorksheet("Rapor").Cell(1, 1).Value = "Seçilen dönemde veri yok.";
        book.SaveAs(path);
    }

    private static void AddSheet(XLWorkbook book, Sheet sheet)
    {
        var name = new string(sheet.Name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        var ws = book.AddWorksheet(name.Length > 31 ? name[..31] : name);
        for (var i = 0; i < sheet.Headers.Length; i++) ws.Cell(1, i + 1).Value = sheet.Headers[i];
        var header = ws.Range(1, 1, 1, Math.Max(1, sheet.Headers.Length)).Style;
        header.Font.Bold = true;
        header.Font.FontColor = XLColor.White;
        header.Fill.BackgroundColor = XLColor.FromHtml("#1D40AF");
        for (var r = 0; r < sheet.Rows.Count; r++)
            for (var c = 0; c < sheet.Rows[r].Length; c++)
            {
                var cell = ws.Cell(r + 2, c + 1);
                switch (sheet.Rows[r][c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                    case int n: cell.Value = n; break;
                    case long n: cell.Value = n; break;
                    case DateTime t: cell.Value = t; cell.Style.DateFormat.Format = "dd.mm.yyyy hh:mm"; break;
                    case var value: cell.Value = value.ToString(); break;
                }
            }
        if (ws.RangeUsed() is { } used) used.SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.ColumnsUsed().AdjustToContents(10, 60);
    }

    private static void WriteCsv(string path, Sheet sheet)
    {
        var text = new StringBuilder();
        void Row(IEnumerable<string> cells) => text.Append(string.Join(";", cells.Select(Quote))).Append("\r\n");
        Row(sheet.Headers);
        foreach (var row in sheet.Rows)
            Row(row.Select(value => value switch
            {
                null => "",
                decimal d => d.ToString("0.00", Turkish),
                DateTime t => t.ToString("dd.MM.yyyy HH:mm", Turkish),
                IFormattable f => f.ToString(null, Turkish),
                var v => v.ToString() ?? ""
            }));
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }

    private static string Quote(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r', ',']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    /// <summary>Dosyayı başlık → değer sözlükleri olarak okur (başlıklar büyük/küçük harf duyarsız).</summary>
    public static List<Dictionary<string, string>> Read(string path)
    {
        var table = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
            ? ReadCsv(path) : ReadExcel(path);
        if (table.Count == 0) return [];
        var headers = table[0].Select(h => Normalize(h)).ToArray();
        var rows = new List<Dictionary<string, string>>();
        foreach (var cells in table.Skip(1))
        {
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
                if (headers[i].Length > 0) row[headers[i]] = i < cells.Count ? cells[i].Trim() : "";
            rows.Add(row);
        }
        return rows;
    }

    public static string Normalize(string header) =>
        new string(header.Trim().ToLower(Turkish).Where(char.IsLetterOrDigit).ToArray());

    private static List<List<string>> ReadExcel(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var book = new XLWorkbook(stream);
        var ws = book.Worksheets.First();
        var used = ws.RangeUsed();
        var result = new List<List<string>>();
        if (used is null) return result;
        var columns = used.LastColumn().ColumnNumber();
        foreach (var row in ws.RowsUsed())
        {
            var cells = new List<string>();
            for (var c = 1; c <= columns; c++)
            {
                var value = row.Cell(c).Value;
                cells.Add(value.IsNumber ? value.GetNumber().ToString(Turkish)
                    : value.IsDateTime ? value.GetDateTime().ToString("dd.MM.yyyy HH:mm", Turkish)
                    : value.IsBoolean ? (value.GetBoolean() ? "1" : "0")
                    : value.ToString());
            }
            result.Add(cells);
        }
        return result;
    }

    private static List<List<string>> ReadCsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var firstLine = text.Split('\n')[0];
        var delimiter = new[] { ';', '\t', ',' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == delimiter) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r'));
                cell.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    /// <summary>Türkçe (1.234,56) veya noktalı (1234.56) sayı.</summary>
    public static bool TryDecimal(string text, out decimal value)
    {
        text = text.Trim().Replace("₺", "").Replace("TL", "", StringComparison.OrdinalIgnoreCase).Replace("%", "").Replace(" ", "").Trim();
        value = 0;
        if (text.Length == 0) return false;
        if (!text.Contains(',') && text.Count(c => c == '.') == 1 && text.Length - text.IndexOf('.') - 1 is >= 1 and <= 2)
            return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        return decimal.TryParse(text, NumberStyles.Number, Turkish, out value);
    }

    public static bool TryDate(string text, out DateTime value) =>
        DateTime.TryParseExact(text.Trim(), ["dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"],
            Turkish, DateTimeStyles.AssumeLocal, out value) || DateTime.TryParse(text, Turkish, DateTimeStyles.AssumeLocal, out value);

    /// <summary>Kimliği olmayan veya Guid olmayan kayıt numarasından sabit kimlik (tekrar içe aktarımda çift kayıt oluşmaz).</summary>
    public static Guid StableId(string kind, string key) =>
        Guid.TryParse(key, out var id) ? id : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(kind + ":" + key)));
}

public sealed partial class MainWindow
{
    private static readonly string[] Datasets = ["Ürünler", "Satışlar", "Teklifler", "Stoklar"];

    private static decimal Lira(long cents) => cents / 100m;

    private static string Cell(Dictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
            if (row.TryGetValue(DataTransfer.Normalize(name), out var value) && value.Length > 0) return value;
        return "";
    }

    private static long? Kurus(Dictionary<string, string> row, params string[] names) =>
        DataTransfer.TryDecimal(Cell(row, names), out var value) ? (long)Math.Round(value * 100, MidpointRounding.AwayFromZero) : null;

    private static int? Whole(Dictionary<string, string> row, params string[] names) =>
        DataTransfer.TryDecimal(Cell(row, names), out var value) ? (int)Math.Round(value) : null;

    private void ShowDataTransfer(string dataset)
    {
        var selected = Array.IndexOf(Datasets, dataset) is >= 0 and var index ? index : 0;
        var root = new StackPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(Field("Veri", new Border
        {
            Child = bar, Background = Paint("#F3F4F6"), CornerRadius = new CornerRadius(10), Padding = new Thickness(4),
            HorizontalAlignment = HorizontalAlignment.Left
        }));
        var buttons = new List<Button>();
        var description = Label("", 13, "#374151");
        description.TextWrapping = TextWrapping.Wrap;
        description.Margin = new Thickness(0, Gap, 0, 0);
        root.Children.Add(description);
        var columns = Label("", 12, Muted);
        columns.TextWrapping = TextWrapping.Wrap;
        columns.Margin = new Thickness(0, SmallGap, 0, 0);
        root.Children.Add(columns);
        void Render()
        {
            for (var i = 0; i < buttons.Count; i++) buttons[i].Style = (Style)FindResource(i == selected ? "SegmentActiveButton" : "SegmentButton");
            var sheet = Export(Datasets[selected]);
            description.Text = Datasets[selected] switch
            {
                "Ürünler" => "Dışa aktarım tüm ürün kartlarını verir. İçe aktarımda ürün kodu eşleşen ürün güncellenir, yeni kod/boş kod yeni ürün olarak eklenir. " +
                    "Kategori yoksa oluşturulur. Fiyat olarak \"KDV hariç fiyat\" veya \"KDV dahil fiyat\" sütunundan biri yeterlidir. Stok sütunları içe aktarımda yok sayılır.",
                "Satışlar" => "Her satır bir satış kalemidir; aynı \"Satış no\" taşıyan satırlar tek satış olur. İçe aktarımda daha önce aktarılmış satış numaraları atlanır " +
                    "(çift kayıt oluşmaz). Ödemeler \"Nakit=100,00; Kart=50,00\" biçimindedir; boşsa tamamı nakit sayılır.",
                "Teklifler" => "Her satır bir teklif kalemidir; aynı \"Teklif no\" taşıyan satırlar tek teklif olur. İçe aktarımda mevcut teklif numaraları atlanır.",
                _ => "Dışa aktarım ürün başına depo ve dükkân stokunu verir. İçe aktarımda \"Depo\" ve \"Dükkân\" sütunlarındaki sayılar sayım sonucu kabul edilir; " +
                    "fark \"Sayım düzeltme\" hareketi olarak kaydedilir."
            };
            columns.Text = "Sütunlar: " + string.Join(" · ", sheet.Headers) + $"\nMevcut kayıt: {sheet.Rows.Count} satır";
        }
        for (var i = 0; i < Datasets.Length; i++)
        {
            var k = i;
            var button = new Button { Content = Datasets[i], Padding = new Thickness(16, 0, 16, 0) };
            if (i > 0) button.Margin = new Thickness(4, 0, 0, 0);
            button.Click += (_, _) => { selected = k; Render(); };
            bar.Children.Add(button);
            buttons.Add(button);
        }
        Render();
        var modal = ShowModal("Excel / CSV aktarımı", root, 720, subtitle: "Dışa aktardığın dosyayı düzenleyip aynı biçimde geri içe aktarabilirsin.");
        Append(modal.Footer, Act("Kapat", modal.Close));
        Append(modal.Footer, Act("İçe aktar", () => { Import(Datasets[selected]); Render(); }, "SuccessButton", ""));
        Append(modal.Footer, Act("CSV'ye aktar", () => Save(Datasets[selected], true), glyph: ""));
        Append(modal.Footer, Act("Excel'e aktar", () => Save(Datasets[selected], false), "PrimaryButton", ""));

        void Save(string name, bool csv)
        {
            var dialog = new SaveFileDialog
            {
                Title = $"{name} dışa aktar", AddExtension = true, DefaultExt = csv ? ".csv" : ".xlsx",
                Filter = csv ? "CSV dosyası (*.csv)|*.csv" : "Excel çalışma kitabı (*.xlsx)|*.xlsx",
                FileName = $"Aden-{name.Replace('ü', 'u').Replace('Ü', 'U').Replace('ş', 's').Replace('ı', 'i')}-{DateTime.Today:yyyyMMdd}"
            };
            if (dialog.ShowDialog(this) != true) return;
            DataTransfer.Write(dialog.FileName, Export(name));
            Toast($"{name} dışa aktarıldı.");
        }
    }

    private DataTransfer.Sheet Export(string dataset) => dataset switch
    {
        "Ürünler" => new("Ürünler",
            ["Ürün kodu", "Ürün adı", "Barkod", "Kategori", "Birim", "KDV hariç fiyat", "KDV oranı", "KDV dahil fiyat", "İndirimli fiyat (KDV hariç)",
             "Raf alt sınırı", "Açıklama", "Varyasyon", "Varyantlar", "Depo stoku", "Dükkân stoku"],
            products.Values.OrderBy(p => p.Name, StringComparer.Create(culture, true)).Select(p => new object?[]
            {
                p.Code, p.Name, HasBarcode(p) ? p.Barcode : "", CategoryPath(CategoryOf(p).Id), p.Unit, Lira(ListNet(p)), VatOf(p), Lira(ListGross(p)),
                p.SalePrice < p.Price ? Lira(SaleNet(p)) : null, p.Minimum, p.Description, VariationName(p), string.Join(", ", OptionsOf(p)),
                stock.GetValueOrDefault(p.Code).Warehouse, stock.GetValueOrDefault(p.Code).Shop
            }).ToList()),
        "Satışlar" => new("Satışlar",
            ["Satış no", "Tarih", "Cari kodu", "Cari", "Ürün kodu", "Ürün", "Adet", "Birim fiyat (KDV dahil)", "KDV oranı", "Birim fiyat (KDV hariç)",
             "İskonto %", "Tutar", "Ödemeler", "Durum"],
            sales.SelectMany(s => s.Value.Lines.Select(l => new object?[]
            {
                s.Id.ToString(), s.Date, s.Value.Account, s.Value.Account.Length > 0 ? AccountName(s.Value.Account) : "", l.Code, l.Name, l.Quantity,
                Lira(l.UnitPrice), l.Vat, l.Net is long n ? Lira(n) : null, l.Discount, Lira((long)l.Quantity * l.UnitPrice),
                string.Join("; ", s.Value.Payments.Select(p => $"{p.Method}={Lira(p.Amount).ToString("0.00", culture)}")), SaleStatus(s)
            })).ToList()),
        "Teklifler" => new("Teklifler",
            ["Teklif no", "Tarih", "Müşteri", "Cari kodu", "İletişim", "Ürün kodu", "Ürün", "Adet", "Birim fiyat (KDV hariç)", "KDV oranı", "İskonto %",
             "Birim fiyat (KDV dahil)", "Tutar"],
            Quotes().OrderByDescending(q => q.Value.Date).SelectMany(q => q.Value.Quote.Lines.Select(l => new object?[]
            {
                q.Key.ToString(), q.Value.Date, q.Value.Quote.Customer, q.Value.Quote.AccountCode, q.Value.Quote.Contact, l.Code, l.Name, l.Quantity,
                Lira(l.Net ?? l.UnitPrice), l.Vat, l.Discount, Lira(l.UnitPrice), Lira((long)l.Quantity * l.UnitPrice)
            })).ToList()),
        _ => new("Stoklar",
            ["Ürün kodu", "Ürün adı", "Barkod", "Birim", "Depo", "Dükkân", "Toplam", "Raf alt sınırı", "Ortalama maliyet (KDV hariç)", "Durum"],
            products.Values.OrderBy(p => p.Name, StringComparer.Create(culture, true)).Select(p =>
            {
                var (w, s) = stock.GetValueOrDefault(p.Code);
                return new object?[]
                {
                    p.Code, p.Name, HasBarcode(p) ? p.Barcode : "", p.Unit, w, s, w + s, p.Minimum,
                    averageCost.TryGetValue(p.Code, out var cost) ? Lira(cost) : null, IsLow(p) ? "Stok azaldı" : ""
                };
            }).ToList())
    };

    private void Import(string dataset)
    {
        var dialog = new OpenFileDialog { Title = $"{dataset} içe aktar", Filter = "Excel veya CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Tüm dosyalar|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        List<Dictionary<string, string>> rows;
        try { rows = DataTransfer.Read(dialog.FileName); }
        catch (Exception error) when (error is IOException or InvalidDataException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException("Dosya okunamadı: " + error.Message);
        }
        if (rows.Count == 0) throw new InvalidOperationException("Dosyada veri satırı yok. İlk satır başlık olmalı.");
        var errors = new List<string>();
        var (added, updated, skipped) = dataset switch
        {
            "Ürünler" => ImportProducts(rows, errors),
            "Satışlar" => ImportSales(rows, errors),
            "Teklifler" => ImportQuotes(rows, errors),
            _ => ImportStock(rows, errors)
        };
        store.Log("İşlem", session.Username, Environment.MachineName, "yerel", $"{dataset} içe aktarıldı",
            $"{Path.GetFileName(dialog.FileName)} · {added} yeni · {updated} güncel · {skipped} atlandı · {errors.Count} hata");
        Refresh();
        if (Connection.IsNetworked(store)) _ = Sync();
        var text = $"{dataset}: {added} yeni, {updated} güncellendi, {skipped} atlandı" + (errors.Count > 0 ? $", {errors.Count} hata." : ".");
        if (errors.Count == 0) { Toast(text); return; }
        var report = Label(text + "\n\n" + string.Join("\n", errors.Take(40)) + (errors.Count > 40 ? $"\n… {errors.Count - 40} hata daha" : ""), 13, "#374151");
        report.TextWrapping = TextWrapping.Wrap;
        var modal = ShowModal("İçe aktarma sonucu", report, 620, 620);
        Append(modal.Footer, Act("Tamam", modal.Close, "PrimaryButton"));
    }

    private (int, int, int) ImportProducts(List<Dictionary<string, string>> rows, List<string> errors)
    {
        int added = 0, updated = 0, skipped = 0, line = 1;
        var used = Read<Product>("product").Select(p => p.Value.Code).ToList();
        foreach (var row in rows)
        {
            line++;
            try
            {
                var name = Cell(row, "Ürün adı", "Ürün", "Ad");
                if (name.Length == 0) throw new InvalidOperationException("Ürün adı boş.");
                var code = Cell(row, "Ürün kodu", "Kod").ToUpperInvariant();
                products.TryGetValue(code, out var existing);
                if (code.Length == 0) { code = NextCode("U-", used); }
                var vat = Whole(row, "KDV oranı", "KDV") ?? existing?.Vat ?? DefaultVat;
                if (vat is < 0 or > 100) throw new InvalidOperationException("KDV oranı 0–100 olmalı.");
                var net = Kurus(row, "KDV hariç fiyat", "Fiyat (KDV hariç)") ?? (Kurus(row, "KDV dahil fiyat", "Satış fiyatı", "Fiyat") is long gross ? WithoutVat(gross, vat) : (long?)null)
                    ?? (existing is null ? throw new InvalidOperationException("Fiyat yok.") : ListNet(existing));
                var discounted = Kurus(row, "İndirimli fiyat (KDV hariç)", "İndirimli fiyat");
                var barcode = Cell(row, "Barkod");
                if (barcode is "-" or "—" or "Barkodsuz") barcode = "";
                if (barcode.Length > 0 && products.Values.Any(p => p.Barcode == barcode && !p.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Barkod {barcode} başka üründe kullanılıyor.");
                var unit = Cell(row, "Birim") is { Length: > 0 } u ? u : existing?.Unit ?? "Adet";
                var category = ImportCategory(Cell(row, "Kategori"), existing?.Category ?? GeneralCategory);
                var variationName = Cell(row, "Varyasyon");
                var variation = variationName.Length == 0 ? existing?.Variation ?? ""
                    : variations.Values.FirstOrDefault(v => string.Compare(v.Name, variationName, culture, CompareOptions.IgnoreCase) == 0)?.Id ?? "";
                var options = Cell(row, "Varyantlar").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var product = new Product(code, name, barcode, net, Whole(row, "Raf alt sınırı", "Minimum") ?? existing?.Minimum ?? 0, false,
                    Cell(row, "Açıklama") is { Length: > 0 } d ? d : existing?.Description ?? "", category,
                    discounted is long value && value < net ? value : null, existing?.Photos, existing?.Cover ?? "", vat, true, unit, variation,
                    variation.Length > 0 && options.Length > 0 ? options : existing?.Options);
                if (existing is not null && existing with { } == product) { skipped++; continue; }
                store.Add("product", product);
                used.Add(code);
                products[code] = product;
                if (existing is null) added++; else updated++;
            }
            catch (InvalidOperationException error) { errors.Add($"Satır {line}: {error.Message}"); }
        }
        return (added, updated, skipped);
    }

    private string ImportCategory(string path, string fallback)
    {
        if (path.Length == 0) return fallback;
        var match = categories.Values.FirstOrDefault(c => string.Compare(CategoryPath(c.Id), path, culture, CompareOptions.IgnoreCase) == 0)
            ?? categories.Values.FirstOrDefault(c => string.Compare(c.Name, path.Split('›').Last().Trim(), culture, CompareOptions.IgnoreCase) == 0);
        if (match is not null) return match.Id;
        var parent = "";
        foreach (var part in path.Split(['›', '>', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var found = categories.Values.FirstOrDefault(c => ParentOf(c).Equals(parent, StringComparison.OrdinalIgnoreCase) &&
                string.Compare(c.Name, part, culture, CompareOptions.IgnoreCase) == 0);
            if (found is null)
            {
                found = new ProductCategory(Guid.NewGuid().ToString("N")[..12], part.Length > 60 ? part[..60] : part, parent);
                store.Add("category", found);
                categories[found.Id] = found;
            }
            parent = found.Id;
        }
        return parent.Length > 0 ? parent : fallback;
    }

    private (int, int, int) ImportSales(List<Dictionary<string, string>> rows, List<string> errors)
    {
        int added = 0, skipped = 0;
        var known = snapshot.Where(e => e.Kind == "sale").Select(e => e.Id).ToHashSet();
        var groups = rows.Select((row, i) => (Row: row, Line: i + 2)).GroupBy(x => Cell(x.Row, "Satış no", "No") is { Length: > 0 } no ? no : $"satır-{x.Line}");
        foreach (var group in groups)
        {
            var first = group.First();
            try
            {
                var id = DataTransfer.StableId("sale", group.Key);
                if (known.Contains(id)) { skipped++; continue; }
                var date = DataTransfer.TryDate(Cell(first.Row, "Tarih"), out var parsed) ? parsed : DateTime.Now;
                var account = Cell(first.Row, "Cari kodu").ToUpperInvariant();
                if (account.Length > 0 && !accounts.ContainsKey(account)) throw new InvalidOperationException($"Cari {account} bulunamadı.");
                var lines = group.Select(x => ImportLine(x.Row, true)).ToArray();
                var total = lines.Sum(l => (long)l.Quantity * l.UnitPrice);
                var payments = ParsePayments(Cell(first.Row, "Ödemeler", "Ödeme"), total);
                if (payments.Any(p => p.Method == "Vadeli") && account.Length == 0) throw new InvalidOperationException("Vadeli ödeme için cari kodu gerekli.");
                store.Add("sale", new Sale(account, lines, payments), id, new DateTimeOffset(date));
                known.Add(id);
                added++;
            }
            catch (InvalidOperationException error) { errors.Add($"Satış {group.Key} (satır {first.Line}): {error.Message}"); }
        }
        return (added, 0, skipped);
    }

    private (int, int, int) ImportQuotes(List<Dictionary<string, string>> rows, List<string> errors)
    {
        int added = 0, skipped = 0;
        var known = snapshot.Where(e => e.Kind == "quote").Select(e => e.Id).ToHashSet();
        var groups = rows.Select((row, i) => (Row: row, Line: i + 2)).GroupBy(x => Cell(x.Row, "Teklif no", "No") is { Length: > 0 } no ? no : $"satır-{x.Line}");
        foreach (var group in groups)
        {
            var first = group.First();
            try
            {
                var id = DataTransfer.StableId("quote", group.Key);
                if (known.Contains(id)) { skipped++; continue; }
                var date = DataTransfer.TryDate(Cell(first.Row, "Tarih"), out var parsed) ? parsed : DateTime.Now;
                var account = Cell(first.Row, "Cari kodu").ToUpperInvariant();
                var customer = Cell(first.Row, "Müşteri", "Cari");
                if (customer.Length == 0 && accounts.TryGetValue(account, out var a)) customer = a.Name;
                if (customer.Length == 0) throw new InvalidOperationException("Müşteri adı boş.");
                var lines = group.Select(x => ImportLine(x.Row, false)).ToArray();
                store.Add("quote", new Quote(customer, Cell(first.Row, "İletişim"), lines, accounts.ContainsKey(account) ? account : ""), id, new DateTimeOffset(date));
                known.Add(id);
                added++;
            }
            catch (InvalidOperationException error) { errors.Add($"Teklif {group.Key} (satır {first.Line}): {error.Message}"); }
        }
        return (added, 0, skipped);
    }

    /// <summary>Satış/teklif kalemi: KDV hariç fiyat + oran + iskonto varsa yeniden fiyatlanır, yoksa KDV dahil birim fiyat kullanılır.</summary>
    private Line ImportLine(Dictionary<string, string> row, bool grossFirst)
    {
        var code = Cell(row, "Ürün kodu", "Kod").ToUpperInvariant();
        var name = Cell(row, "Ürün", "Ürün adı");
        products.TryGetValue(code, out var product);
        if (code.Length == 0 && name.Length > 0) { try { product = Find(name); code = product.Code; } catch (InvalidOperationException) { } }
        if (code.Length == 0) throw new InvalidOperationException("Ürün kodu boş.");
        if (name.Length == 0) name = product?.Name ?? code;
        var quantity = Whole(row, "Adet", "Miktar") ?? throw new InvalidOperationException("Adet yok.");
        if (quantity is < 1 or > 1_000_000) throw new InvalidOperationException("Adet 1 veya üzeri olmalı.");
        var vat = Whole(row, "KDV oranı", "KDV") ?? (product is null ? DefaultVat : VatOf(product));
        if (vat is < 0 or > 100) throw new InvalidOperationException("KDV oranı 0–100 olmalı.");
        var discount = DataTransfer.TryDecimal(Cell(row, "İskonto %", "İskonto"), out var d) ? d : 0;
        if (discount is < 0 or > 100) throw new InvalidOperationException("İskonto 0–100 olmalı.");
        var net = Kurus(row, "Birim fiyat (KDV hariç)", "KDV hariç birim");
        var gross = Kurus(row, "Birim fiyat (KDV dahil)", "Birim fiyat", "Fiyat");
        if (grossFirst && gross is long g && net is null) return new Line(code, name, quantity, g, vat, WithoutVat(g, vat), 0);
        if (net is long n) return Priced(code, name, quantity, n, vat, discount);
        if (gross is long g2) return new Line(code, name, quantity, g2, vat, WithoutVat(g2, vat), 0);
        if (product is not null) return LineFor(product, quantity, discount);
        throw new InvalidOperationException("Birim fiyat yok.");
    }

    private static Payment[] ParsePayments(string text, long total)
    {
        if (total == 0) return [];
        if (text.Trim().Length == 0) return [new Payment("Nakit", total, "")];
        var payments = new List<Payment>();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(['=', ':'], 2, StringSplitOptions.TrimEntries);
            var method = pieces[0] switch
            {
                var m when m.Equals("Kredi Kartı", StringComparison.OrdinalIgnoreCase) || m.Equals("Kart", StringComparison.OrdinalIgnoreCase) => "Kart",
                var m when m.StartsWith("Havale", StringComparison.OrdinalIgnoreCase) || m.Equals("EFT", StringComparison.OrdinalIgnoreCase) => "Havale/EFT",
                var m when m.Equals("Vadeli", StringComparison.OrdinalIgnoreCase) => "Vadeli",
                var m when m.Equals("Nakit", StringComparison.OrdinalIgnoreCase) => "Nakit",
                var m => throw new InvalidOperationException($"Ödeme yöntemi tanınmadı: {m}")
            };
            var amount = pieces.Length > 1 && DataTransfer.TryDecimal(pieces[1], out var value) ? (long)Math.Round(value * 100) : total - payments.Sum(p => p.Amount);
            if (amount > 0) payments.Add(new Payment(method, amount, ""));
        }
        if (payments.Sum(p => p.Amount) != total)
            throw new InvalidOperationException($"Ödemeler toplamı ({payments.Sum(p => p.Amount) / 100m:N2}) satış toplamına ({total / 100m:N2}) eşit değil.");
        return payments.ToArray();
    }

    private (int, int, int) ImportStock(List<Dictionary<string, string>> rows, List<string> errors)
    {
        int updated = 0, skipped = 0, line = 1;
        foreach (var row in rows)
        {
            line++;
            try
            {
                var code = Cell(row, "Ürün kodu", "Kod").ToUpperInvariant();
                var barcode = Cell(row, "Barkod");
                var product = products.TryGetValue(code, out var byCode) ? byCode
                    : barcode.Length > 0 ? products.Values.FirstOrDefault(p => p.Barcode == barcode) : null;
                if (product is null) throw new InvalidOperationException($"Ürün bulunamadı: {code} {barcode}".Trim());
                var current = stock.GetValueOrDefault(product.Code);
                var warehouse = Whole(row, "Depo", "Depo stoku") ?? (int)current.Warehouse;
                var shop = Whole(row, "Dükkân", "Dükkân stoku", "Dukkan") ?? (int)current.Shop;
                if (warehouse < 0 || shop < 0) throw new InvalidOperationException("Stok eksi olamaz.");
                var dw = warehouse - current.Warehouse;
                var ds = shop - current.Shop;
                if (dw == 0 && ds == 0) { skipped++; continue; }
                store.Add("stock", new Stock(product.Code, (int)dw, (int)ds, 0, "Excel/CSV sayım aktarımı", Type: "Sayım"));
                stock[product.Code] = (warehouse, shop);
                updated++;
            }
            catch (InvalidOperationException error) { errors.Add($"Satır {line}: {error.Message}"); }
        }
        return (0, updated, skipped);
    }
}
