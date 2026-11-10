using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace Aden;

/// <summary>
/// Fiyat hesabı: KDV hariç fiyat − indirim/iskonto = matrah; matrah + KDV = KDV dahil fiyat.
/// Tutarlar kuruş cinsinden; birim fiyatlar kuruşa yuvarlanır, toplamlar birim fiyatlardan toplanır.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly int[] VatRates = [0, 1, 10, 20];

    private int DefaultVat => int.TryParse(store.Setting("defaultVat", "20"), out var rate) && rate is >= 0 and <= 100 ? rate : 20;

    private int VatOf(Product product) => product.Vat ?? DefaultVat;

    private static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
    private static long WithVat(long net, int vat) => Round(net * (100m + vat) / 100m);
    private static long WithoutVat(long gross, int vat) => Round(gross * 100m / (100m + vat));
    private static long AfterDiscount(long net, decimal percent) => Round(net * (100m - percent) / 100m);

    /// <summary>Liste fiyatı (indirimsiz), KDV hariç.</summary>
    private long ListNet(Product product) => product.NetPrice ? product.Price : WithoutVat(product.Price, VatOf(product));
    /// <summary>Satış fiyatı (ürün indirimi uygulanmış), KDV hariç.</summary>
    private long SaleNet(Product product) => product.NetPrice ? product.SalePrice : WithoutVat(product.SalePrice, VatOf(product));
    private long ListGross(Product product) => product.NetPrice ? WithVat(product.Price, VatOf(product)) : product.Price;
    private long SaleGross(Product product) => product.NetPrice ? WithVat(product.SalePrice, VatOf(product)) : product.SalePrice;

    /// <summary>Üründen satır: ürün indirimi dahil KDV hariç fiyat + iskonto + KDV.</summary>
    private Line LineFor(Product product, int quantity, decimal discount = 0) =>
        Priced(product.Code, product.Name, quantity, SaleNet(product), VatOf(product), discount);

    private static Line Priced(string code, string name, int quantity, long net, int vat, decimal discount) =>
        new(code, name, quantity, WithVat(AfterDiscount(net, discount), vat), vat, net, discount);

    /// <summary>Aynı satırı yeni iskontoyla yeniden fiyatlar (eski satırlar KDV'siz kabul edilir).</summary>
    private static Line Reprice(Line line, decimal discount) =>
        Priced(line.Code, line.Name, line.Quantity, line.Net ?? line.UnitPrice, line.Vat ?? 0, discount);

    private readonly record struct Breakdown(long Subtotal, long Discount, long Vat, long Total);

    /// <summary>Ara toplam (iskonto öncesi KDV hariç), iskonto, KDV ve genel toplam. Ara − İskonto + KDV = Genel.</summary>
    private static Breakdown Summarize(IEnumerable<Line> lines)
    {
        long subtotal = 0, discount = 0, vat = 0, total = 0;
        foreach (var line in lines)
        {
            var net = line.Net ?? line.UnitPrice;
            var after = line.Net is null ? line.UnitPrice : AfterDiscount(net, line.Discount);
            subtotal += line.Quantity * net;
            discount += line.Quantity * (net - after);
            vat += line.Quantity * (line.UnitPrice - after);
            total += line.Quantity * line.UnitPrice;
        }
        return new(subtotal, discount, vat, total);
    }

    private bool TryPercent(string text, out decimal value) =>
        decimal.TryParse(text.Trim().TrimStart('%'), NumberStyles.Number, culture, out value) && value is >= 0 and <= 100;

    /// <summary>Yazılabilir KDV oranı seçici (0, 1, 10, 20 veya özel).</summary>
    private ComboBox VatBox(int rate)
    {
        var box = new ComboBox { IsEditable = true, ItemsSource = VatRates.Select(r => r.ToString(culture)).ToArray() };
        box.Text = rate.ToString(culture);
        return box;
    }

    private int ReadVat(ComboBox box) =>
        int.TryParse(box.Text.Trim().TrimStart('%'), NumberStyles.Integer, culture, out var rate) && rate is >= 0 and <= 100
            ? rate : throw new InvalidOperationException("KDV oranı 0 ile 100 arasında tam sayı olmalı.");

    /// <summary>Sağa hizalı tutar özeti satırı.</summary>
    private static Grid SummaryRow(string caption, out TextBlock value, double size = 13, bool strong = false, string color = Ink)
    {
        var row = new Grid { Margin = new Thickness(0, strong ? SmallGap : 4, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Label(caption, size, strong ? Ink : Muted, strong);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        value = Label("", size, color, true);
        value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        return row;
    }
}
