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
                    product.Cover is not null && product.DiscountedPrice is null or (>= 0 and <= 10_000_000_000) &&
                    (product.Photos is null || product.Photos is { Length: <= 500 } && product.Photos.All(Text)),
                "category" => JsonSerializer.Deserialize<ProductCategory>(entry.Data) is { } category &&
                    Text(category.Id) && Text(category.Name) && category.Parent is not null && category.Parent != category.Id,
                "account" => JsonSerializer.Deserialize<Account>(entry.Data) is { } account &&
                    Text(account.Code) && Text(account.Name) && account.Contact is not null &&
                    account.Type is "Kurumsal" or "Bireysel" &&
                    (account.Phones is null || account.Phones.All(p => p is not null && Text(p.Number))) &&
                    (account.Emails is null || account.Emails.All(e => e is not null && Text(e.Address))),
                "stock" => JsonSerializer.Deserialize<Stock>(entry.Data) is { } stock &&
                    Text(stock.Code) && stock.Reference is not null && stock.UnitCost is >= 0 and <= 10_000_000_000 &&
                    ((stock.Warehouse > 0 && stock.Shop == 0) ||
                     (stock.Warehouse < 0 && (long)stock.Warehouse + stock.Shop == 0)),
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
                _ => false
            };
        }
        catch (JsonException) { return false; }
    }

    private static bool QuoteIsValid(Quote quote) =>
        Text(quote.Customer) && quote.Contact is not null && LinesAreValid(quote.Lines);

    private static bool SaleIsValid(Sale sale) =>
        sale.Account is not null && LinesAreValid(sale.Lines) &&
        sale.Payments is { Length: <= 200 } &&
        sale.Payments.All(p => p is not null && p.Amount is > 0 and <= 10_000_000_000 &&
            p.Reference is not null && (p.Method == "Nakit" ||
            p.Method == "Kart" || (p.Method == "Vadeli" && Text(sale.Account)))) &&
        sale.Lines.Sum(l => (decimal)l.Quantity * l.UnitPrice) == sale.Payments.Sum(p => p.Amount);

    private static bool LinesAreValid(Line[]? lines) =>
        lines is { Length: > 0 and <= 200 } && lines.All(l =>
            l is not null && Text(l.Code) && Text(l.Name) && l.Quantity is > 0 and <= 1_000_000 &&
            l.UnitPrice is >= 0 and <= 10_000_000_000);

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value);
}
