using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
    private readonly ContentControl host = new() { Focusable = false };
    private readonly Dictionary<string, Product> products = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Account> accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (long Warehouse, long Shop)> stock = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<Line> basket = new();
    private readonly ObservableCollection<Payment> payments = new();
    private readonly ObservableCollection<Line> quoteLines = new();
    private readonly List<(DataGrid Grid, Func<object[]> Data)> views = new();
    private readonly CultureInfo culture = CultureInfo.GetCultureInfo("tr-TR");
    private bool syncing;

    public MainWindow(Store store)
    {
        this.store = store;
        Title = "Aden | Ön Muhasebe, Stok ve Satış";
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("tr-TR");
        AddSales();
        AddStock();
        AddProducts();
        AddAccounts();
        AddQuotes();
        AddReports();
        AddSettings();
        BuildWorkspace();
        Refresh();
        Totals();
        status.Text = store.Setting("mode") == "sync" ? "Eşitleme açılıyor" : "Yerel mod · Veriler bu bilgisayarda";
        timer.Tick += async (_, _) => await Sync();
        timer.Start();
        Loaded += async (_, _) => { saleSearch?.Focus(); await Sync(); };
        Closed += (_, _) => { timer.Stop(); closing.Cancel(); store.Dispose(); };
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
        foreach (var item in Read<Product>("product"))
        {
            if (item.Value.Archived) products.Remove(item.Value.Code);
            else products[item.Value.Code] = item.Value;
        }
        foreach (var item in Read<Account>("account"))
        {
            if (item.Value.Archived) accounts.Remove(item.Value.Code);
            else accounts[item.Value.Code] = item.Value;
        }
        LoadCategories();
        foreach (var item in Read<Stock>("stock")) Move(item.Value.Code, item.Value.Warehouse, item.Value.Shop);
        foreach (var sale in Read<Sale>("sale"))
            foreach (var line in sale.Value.Lines) Move(line.Code, 0, -line.Quantity);
        foreach (var view in views) view.Grid.ItemsSource = view.Data();
        foreach (var refresh in refreshers) refresh();
        refreshCatalog?.Invoke();
        refreshCart?.Invoke();
    }

    private void Move(string code, int warehouse, int shop)
    {
        var old = stock.GetValueOrDefault(code);
        stock[code] = (old.Warehouse + warehouse, old.Shop + shop);
    }

    private Product Find(string value)
    {
        if (products.TryGetValue(value, out var product)) return product;
        var barcode = value == DefaultBarcode ? null : products.Values.FirstOrDefault(p => p.Barcode == value);
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
}
