using System.Text.Json;

namespace Aden;

/// <summary>
/// Kayıtlardan türetilen güncel durum: düzenlenmiş/silinmiş satışlar, iadeler, stok hareketleri,
/// cari hareketleri, ortalama maliyetler ve vadeli alım taksitleri.
/// </summary>
public sealed partial class MainWindow
{
    private sealed record SaleRecord(Guid Id, DateTime Date, Sale Value, bool Edited);
    private sealed record ReturnRecord(Guid Id, DateTime Date, SaleReturn Value);
    private sealed record MoveRecord(Guid Id, DateTime Date, Stock Value, bool Edited);
    private sealed record SupplierReturnRecord(Guid Id, DateTime Date, SupplierReturn Value);
    /// <summary>Debit: carinin bize borcunu artırır (alacağımız). Credit: azaltır (borcumuz artar).</summary>
    private sealed record AccountMove(DateTime Date, string Kind, string Detail, long Debit, long Credit);

    private List<SaleRecord> sales = new();
    private List<ReturnRecord> returns = new();
    private List<MoveRecord> movements = new();
    private List<SupplierReturnRecord> supplierReturns = new();
    private readonly Dictionary<string, long> averageCost = new(StringComparer.OrdinalIgnoreCase);

    private void LoadLedger()
    {
        var saleMap = new Dictionary<Guid, SaleRecord>();
        var order = new List<Guid>();
        var moveMap = new Dictionary<Guid, MoveRecord>();
        var moveOrder = new List<Guid>();
        returns = new();
        supplierReturns = new();
        foreach (var entry in snapshot)
        {
            var date = entry.CreatedAt.LocalDateTime;
            switch (entry.Kind)
            {
                case "sale":
                    saleMap[entry.Id] = new(entry.Id, date, JsonSerializer.Deserialize<Sale>(entry.Data)!, false);
                    order.Add(entry.Id);
                    break;
                case "sale-revision":
                    var revision = JsonSerializer.Deserialize<SaleRevision>(entry.Data)!;
                    if (!saleMap.TryGetValue(revision.SaleId, out var original)) break;
                    if (revision.Value is null) saleMap.Remove(revision.SaleId);
                    else saleMap[revision.SaleId] = original with { Value = revision.Value, Edited = true };
                    break;
                case "return":
                    returns.Add(new(entry.Id, date, JsonSerializer.Deserialize<SaleReturn>(entry.Data)!));
                    break;
                case "stock":
                    moveMap[entry.Id] = new(entry.Id, date, JsonSerializer.Deserialize<Stock>(entry.Data)!, false);
                    moveOrder.Add(entry.Id);
                    break;
                case "stock-revision":
                    var change = JsonSerializer.Deserialize<StockRevision>(entry.Data)!;
                    if (!moveMap.TryGetValue(change.StockId, out var move)) break;
                    if (change.Value is null) moveMap.Remove(change.StockId);
                    else moveMap[change.StockId] = move with { Value = change.Value, Edited = true };
                    break;
                case "supplier-return":
                    supplierReturns.Add(new(entry.Id, date, JsonSerializer.Deserialize<SupplierReturn>(entry.Data)!));
                    break;
            }
        }
        sales = order.Where(saleMap.ContainsKey).Select(id => saleMap[id]).ToList();
        movements = moveOrder.Where(moveMap.ContainsKey).Select(id => moveMap[id]).ToList();
        // Silinen satışa bağlı iadeler geçersizdir.
        returns = returns.Where(r => saleMap.ContainsKey(r.Value.SaleId)).ToList();

        foreach (var move in movements) Move(move.Value.Code, move.Value.Warehouse, move.Value.Shop);
        foreach (var sale in sales)
            foreach (var line in sale.Value.Lines) Move(line.Code, 0, -line.Quantity);
        foreach (var item in returns)
        {
            foreach (var line in item.Value.Returned) Move(line.Code, 0, line.Quantity);
            foreach (var line in item.Value.Given) Move(line.Code, 0, -line.Quantity);
        }
        foreach (var item in supplierReturns)
            foreach (var line in item.Value.Lines)
                Move(line.Code, item.Value.Source == "Depo" ? -line.Quantity : 0, item.Value.Source == "Depo" ? 0 : -line.Quantity);

        averageCost.Clear();
        foreach (var group in movements.Where(m => IsReceipt(m.Value)).GroupBy(m => m.Value.Code, StringComparer.OrdinalIgnoreCase))
        {
            var quantity = group.Sum(m => (long)m.Value.Warehouse);
            if (quantity > 0) averageCost[group.Key] = Round(group.Sum(m => (decimal)m.Value.Warehouse * NetCost(m.Value)) / quantity);
        }
    }

    private static bool IsReceipt(Stock stock) => stock.Type.Length == 0 && stock.Warehouse > 0 && stock.Shop == 0;

    /// <summary>Birim alış maliyeti, KDV hariç.</summary>
    private long NetCost(Stock stock) => stock.NetCost ?? WithoutVat(stock.UnitCost, stock.Vat ?? DefaultVat);

    private static long ReceiptTotal(Stock stock) => (long)stock.Warehouse * stock.UnitCost;

    /// <summary>Mal kabulde peşin ödenen tutar.</summary>
    private static long PaidNow(Stock stock) => stock.Status switch
    {
        "Borç" or "Vadeli" => 0,
        "Parçalı" => stock.Paid,
        _ => ReceiptTotal(stock)
    };

    /// <summary>Vadeli alımın aylık taksitleri: ilk taksit alımdan bir ay sonra.</summary>
    private static IEnumerable<(DateTime Due, int Index, long Amount)> Installments(MoveRecord move)
    {
        if (move.Value.Status != "Vadeli" || move.Value.Term <= 0) yield break;
        var total = ReceiptTotal(move.Value);
        var each = total / move.Value.Term;
        for (var i = 1; i <= move.Value.Term; i++)
            yield return (move.Date.AddMonths(i), i, i == move.Value.Term ? total - each * (move.Value.Term - 1) : each);
    }

    /// <summary>Alım ödemeleri: peşin ödenenler ve vadesi gelen taksitler (gider olarak raporlanır).</summary>
    private IEnumerable<(DateTime Date, string Detail, string Account, long Amount)> PurchasePayments()
    {
        foreach (var move in movements.Where(m => IsReceipt(m.Value)))
        {
            var name = ProductName(move.Value.Code);
            var supplier = SupplierName(move.Value);
            if (PaidNow(move.Value) > 0)
                yield return (move.Date, $"Mal alımı · {name} × {move.Value.Warehouse}" + (move.Value.Method.Length > 0 ? $" · {move.Value.Method}" : ""),
                    supplier, PaidNow(move.Value));
            foreach (var (due, index, amount) in Installments(move))
                yield return (due, $"Vade taksiti {index}/{move.Value.Term} · {name} × {move.Value.Warehouse}", supplier, amount);
        }
    }

    private string ProductName(string code) => products.TryGetValue(code, out var product) ? product.Name : code;

    private string SupplierName(Stock stock) =>
        stock.Account.Length > 0 && accounts.TryGetValue(stock.Account, out var account) ? account.Name
        : stock.Supplier.Length > 0 ? stock.Supplier : stock.Account;

    private string AccountName(string code) => accounts.TryGetValue(code, out var account) ? account.Name : code;

    /// <summary>Carinin tüm hareketleri, tarih sırasıyla.</summary>
    private List<AccountMove> AccountMoves(string code)
    {
        var moves = new List<AccountMove>();
        bool Same(string value) => value.Equals(code, StringComparison.OrdinalIgnoreCase);
        foreach (var sale in sales.Where(s => Same(s.Value.Account)))
        {
            var credit = sale.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount);
            if (credit > 0)
                moves.Add(new(sale.Date, "Vadeli satış", $"{sale.Value.Lines.Sum(l => l.Quantity)} ürün · {Format(Sum(sale.Value.Lines))}", credit, 0));
        }
        foreach (var item in returns)
        {
            var sale = sales.FirstOrDefault(s => s.Id == item.Value.SaleId);
            if (sale is null || !Same(sale.Value.Account)) continue;
            var amount = item.Value.Payments.Where(p => p.Method == "Vadeli").Sum(p => p.Amount);
            if (amount != 0)
                moves.Add(new(item.Date, $"Satış {item.Value.Type.ToLower(culture)}", item.Value.Reason, Math.Max(amount, 0), Math.Max(-amount, 0)));
        }
        foreach (var cash in Read<Cash>("cash").Where(c => Same(c.Value.Account)))
            moves.Add(new(cash.Entry.CreatedAt.LocalDateTime, cash.Value.Category, cash.Value.Detail,
                cash.Value.Amount < 0 ? -cash.Value.Amount : 0, cash.Value.Amount > 0 ? cash.Value.Amount : 0));
        foreach (var move in movements.Where(m => IsReceipt(m.Value) && Same(m.Value.Account)))
        {
            var name = $"{ProductName(move.Value.Code)} × {move.Value.Warehouse}" + (move.Value.Reference.Length > 0 ? $" · {move.Value.Reference}" : "");
            moves.Add(new(move.Date, "Mal alımı", name, 0, ReceiptTotal(move.Value)));
            if (PaidNow(move.Value) > 0)
                moves.Add(new(move.Date, "Alım ödemesi", $"{move.Value.Method} · {name}".Trim(' ', '·'), PaidNow(move.Value), 0));
            foreach (var (due, index, amount) in Installments(move).Where(i => i.Due.Date <= DateTime.Today))
                moves.Add(new(due, "Vade taksiti", $"{index}/{move.Value.Term} · {name}", amount, 0));
        }
        foreach (var item in supplierReturns.Where(r => Same(r.Value.Account)))
            moves.Add(new(item.Date, "Tedarikçiye iade", $"{item.Value.Number} · {item.Value.Lines.Sum(l => l.Quantity)} ürün",
                item.Value.Lines.Sum(l => (long)l.Quantity * l.UnitCost), 0));
        return moves.OrderBy(m => m.Date).ToList();
    }

    /// <summary>Pozitif: cari bize borçlu (alacağımız). Negatif: biz cariye borçluyuz.</summary>
    private long AccountBalance(string code) => AccountMoves(code).Sum(m => m.Debit - m.Credit);

    /// <summary>Satırın iskonto sonrası KDV hariç birim tutarı.</summary>
    private static long LineNet(Line line) => line.Net is long net ? AfterDiscount(net, line.Discount) : line.UnitPrice;

    /// <summary>Satış ve iade/değişim satırları: (tarih, satış no, satır, işaret). İade edilen satırlar −1 ile gelir.</summary>
    private IEnumerable<(DateTime Date, Guid Sale, Line Line, int Sign)> SoldLines()
    {
        foreach (var sale in sales)
            foreach (var line in sale.Value.Lines) yield return (sale.Date, sale.Id, line, 1);
        foreach (var item in returns)
        {
            foreach (var line in item.Value.Returned) yield return (item.Date, item.Value.SaleId, line, -1);
            foreach (var line in item.Value.Given) yield return (item.Date, item.Value.SaleId, line, 1);
        }
    }

    private bool IsLow(Product product) => product.Minimum > 0 && stock.GetValueOrDefault(product.Code).Shop <= product.Minimum;

    private static bool HasBarcode(Product product) => product.Barcode.Length > 0 && product.Barcode != DefaultBarcode;
}
