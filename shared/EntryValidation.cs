using System.Text.Json;

namespace Aden;

public static class EntryValidation
{
    public static bool IsValid(Entry? entry)
    {
        if (entry is null || entry.Id == Guid.Empty || entry.CreatedAt == default ||
            string.IsNullOrWhiteSpace(entry.Data) || entry.Data.Length > 100_000)
            return false;

        try
        {
            return entry.Kind switch
            {
                "product" => JsonSerializer.Deserialize<Product>(entry.Data) is { } product &&
                    Text(product.Code) && Text(product.Name) && product.Barcode is not null &&
                    product.Price is >= 0 and <= 10_000_000_000 && product.Minimum >= 0 &&
                    product.Barcode.Length <= 128 && product.Description is not null && product.Category is not null &&
                    product.Cover is not null && product.DiscountedPrice is null or (>= 0 and <= 10_000_000_000) && product.Vat is null or (>= 0 and <= 100) &&
                    (product.Photos is null || product.Photos is { Length: <= 500 } && product.Photos.All(Text)) &&
                    Text(product.Unit) && product.Unit.Length <= 30 && product.Variation is not null &&
                    (product.Options is null || product.Options is { Length: <= 200 } && product.Options.All(Text)),
                "variation" => JsonSerializer.Deserialize<VariationType>(entry.Data) is { } variation &&
                    Text(variation.Id) && Text(variation.Name) && variation.Name.Length <= 60 &&
                    variation.Values is { Length: <= 200 } && variation.Values.All(v => Text(v) && v.Length <= 60),
                "category" => JsonSerializer.Deserialize<ProductCategory>(entry.Data) is { } category &&
                    Text(category.Id) && Text(category.Name) && category.Parent is not null && category.Parent != category.Id,
                "account" => JsonSerializer.Deserialize<Account>(entry.Data) is { } account &&
                    Text(account.Code) && Text(account.Name) && account.Contact is not null &&
                    account.Type is "Kurumsal" or "Bireysel" or "Tedarikçi" &&
                    (account.Phones is null || account.Phones.All(p => p is not null && Text(p.Number))) &&
                    (account.Emails is null || account.Emails.All(e => e is not null && Text(e.Address))),
                "stock" => JsonSerializer.Deserialize<Stock>(entry.Data) is { } stock && StockIsValid(stock),
                "stock-revision" => JsonSerializer.Deserialize<StockRevision>(entry.Data) is { } stockRevision &&
                    stockRevision.StockId != Guid.Empty && (stockRevision.Value is null || StockIsValid(stockRevision.Value)),
                "sale-revision" => JsonSerializer.Deserialize<SaleRevision>(entry.Data) is { } saleRevision &&
                    saleRevision.SaleId != Guid.Empty && saleRevision.Reason is not null &&
                    (saleRevision.Value is null || SaleIsValid(saleRevision.Value)),
                "return" => JsonSerializer.Deserialize<SaleReturn>(entry.Data) is { } saleReturn && ReturnIsValid(saleReturn),
                "supplier-return" => JsonSerializer.Deserialize<SupplierReturn>(entry.Data) is { } supplierReturn &&
                    Text(supplierReturn.Number) && supplierReturn.Account is not null && supplierReturn.Supplier is not null &&
                    (Text(supplierReturn.Account) || Text(supplierReturn.Supplier)) && supplierReturn.Reason is not null &&
                    supplierReturn.Source is "Depo" or "Dükkân" && supplierReturn.Lines is { Length: > 0 and <= 200 } &&
                    supplierReturn.Lines.All(l => l is not null && Text(l.Code) && Text(l.Name) &&
                        l.Quantity is > 0 and <= 1_000_000 && l.UnitCost is >= 0 and <= 10_000_000_000),
                "sale" => JsonSerializer.Deserialize<Sale>(entry.Data) is { } sale && SaleIsValid(sale),
                "quote" => JsonSerializer.Deserialize<Quote>(entry.Data) is { } quote &&
                    QuoteIsValid(quote),
                "quote-revision" => JsonSerializer.Deserialize<QuoteRevision>(entry.Data) is { } revision &&
                    revision.QuoteId != Guid.Empty && (revision.Value is null || QuoteIsValid(revision.Value)),
                "cash" => JsonSerializer.Deserialize<Cash>(entry.Data) is { } cash &&
                    Text(cash.Detail) && cash.Account is not null && cash.Category switch
                    {
                        "Gelir" => cash.Account.Length == 0 && cash.Amount is > 0 and <= 10_000_000_000,
                        "Gider" => cash.Account.Length == 0 && cash.Amount is < 0 and >= -10_000_000_000,
                        "Tahsilat" => Text(cash.Account) && cash.Amount is > 0 and <= 10_000_000_000,
                        "Cari ödeme" => Text(cash.Account) && cash.Amount is < 0 and >= -10_000_000_000,
                        _ => false
                    },
                "user" => JsonSerializer.Deserialize<UserAccount>(entry.Data) is { } user &&
                    Users.ValidUsername(user.Username) && user.Username == Users.Normalize(user.Username) &&
                    user.Username != Users.Developer && Text(user.Name) && user.Name.Length <= 120 &&
                    user.Role is Users.AdminRole or Users.UserRole && Text(user.Hash) && Text(user.Salt),
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }

    public static readonly string[] SaleMethods = ["Nakit", "Kart", "Havale/EFT", "Vadeli"];
    public static readonly string[] PurchaseMethods = ["Nakit", "Kredi Kartı", "Havale/EFT"];
    public static readonly string[] PurchaseStatuses = ["Ödendi", "Borç", "Parçalı", "Vadeli"];

    private static bool StockIsValid(Stock stock)
    {
        if (!Text(stock.Code) || stock.Reference is null || stock.UnitCost is < 0 or > 10_000_000_000 ||
            stock.Account is null || stock.Supplier is null || stock.Status is null || stock.Method is null ||
            stock.Type is null || stock.Paid < 0 || stock.Term is < 0 or > 120)
            return false;
        if (stock.Type == "Sayım") return stock.Warehouse != 0 || stock.Shop != 0;
        if (stock.Type.Length > 0) return false;
        if (stock.Warehouse < 0) return (long)stock.Warehouse + stock.Shop == 0;
        if (stock.Warehouse <= 0 || stock.Shop != 0) return false;
        var total = (decimal)stock.Warehouse * stock.UnitCost;
        return (stock.Status.Length == 0 || PurchaseStatuses.Contains(stock.Status)) &&
            (stock.Method.Length == 0 || PurchaseMethods.Contains(stock.Method)) &&
            stock.Paid <= total && (stock.Status != "Vadeli" || stock.Term > 0) &&
            (stock.Status is not ("Borç" or "Parçalı" or "Vadeli") || Text(stock.Account) || Text(stock.Supplier));
    }

    private static bool ReturnIsValid(SaleReturn value) =>
        value.SaleId != Guid.Empty && value.Type is "İade" or "Değişim" or "İptal" && value.Reason is not null &&
        value.Returned is not null && value.Given is not null && value.Returned.Length + value.Given.Length > 0 &&
        (value.Returned.Length == 0 || LinesAreValid(value.Returned)) && (value.Given.Length == 0 || LinesAreValid(value.Given)) &&
        value.Payments is { Length: <= 200 } &&
        value.Payments.All(p => p is not null && p.Amount != 0 && Math.Abs(p.Amount) <= 10_000_000_000 &&
            p.Reference is not null && SaleMethods.Contains(p.Method)) &&
        value.Given.Sum(l => (decimal)l.Quantity * l.UnitPrice) - value.Returned.Sum(l => (decimal)l.Quantity * l.UnitPrice) ==
            value.Payments.Sum(p => (decimal)p.Amount);

    private static bool QuoteIsValid(Quote quote) =>
        Text(quote.Customer) && quote.Contact is not null && LinesAreValid(quote.Lines);

    private static bool SaleIsValid(Sale sale) =>
        sale.Account is not null && LinesAreValid(sale.Lines) &&
        sale.Payments is { Length: <= 200 } &&
        sale.Payments.All(p => p is not null && p.Amount is > 0 and <= 10_000_000_000 &&
            p.Reference is not null && SaleMethods.Contains(p.Method) && (p.Method != "Vadeli" || Text(sale.Account))) &&
        sale.Lines.Sum(l => (decimal)l.Quantity * l.UnitPrice) == sale.Payments.Sum(p => p.Amount);

    private static bool LinesAreValid(Line[]? lines) =>
        lines is { Length: > 0 and <= 200 } && lines.All(l =>
            l is not null && Text(l.Code) && Text(l.Name) && l.Quantity is > 0 and <= 1_000_000 &&
            l.UnitPrice is >= 0 and <= 10_000_000_000 && l.Vat is null or (>= 0 and <= 100) &&
            l.Net is null or (>= 0 and <= 10_000_000_000) && l.Discount is >= 0 and <= 100);

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value);
}
