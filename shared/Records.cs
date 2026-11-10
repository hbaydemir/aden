using System.Text.Json.Serialization;

namespace Aden;

public record Entry(Guid Id, string Kind, DateTimeOffset CreatedAt, string Data);
public record Change(long Sequence, Entry Entry);
public record SyncPage(long Cursor, Change[] Changes);
public record Product(string Code, string Name, string Barcode, long Price, int Minimum, bool Archived = false,
    string Description = "", string Category = "", long? DiscountedPrice = null, string[]? Photos = null, string Cover = "",
    int? Vat = null, bool NetPrice = false)
{
    // NetPrice=true: Price ve DiscountedPrice KDV hariçtir. Eski kayıtlarda (false) KDV dahildir.
    /// <summary>Sepete yansıyan fiyat: indirim varsa indirimli fiyat.</summary>
    [JsonIgnore] public long SalePrice => DiscountedPrice is long discounted && discounted < Price ? discounted : Price;
}
public record ProductCategory(string Id, string Name, string Parent, bool Archived = false);
public record Phone(string Kind, string Number);
public record Email(string Name, string Address);
public record Account(string Code, string Name, string Contact, bool Archived = false,
    string Type = "Kurumsal", Phone[]? Phones = null, Email[]? Emails = null, string TaxNumber = "", string TaxOffice = "",
    string BillingAddress = "", string Address = "", bool SameAddress = false, string Website = "");
public record Stock(string Code, int Warehouse, int Shop, long UnitCost, string Reference, int? Vat = null, long? NetCost = null);
/// <summary>UnitPrice: KDV dahil, iskonto sonrası birim fiyat. Net: iskonto öncesi KDV hariç birim fiyat.
/// Discount: Net'e uygulanan iskonto yüzdesi. Eski kayıtlarda Vat/Net boştur.</summary>
public record Line(string Code, string Name, int Quantity, long UnitPrice, int? Vat = null, long? Net = null, decimal Discount = 0);
public record Payment(string Method, long Amount, string Reference);
public record Sale(string Account, Line[] Lines, Payment[] Payments);
public record Quote(string Customer, string Contact, Line[] Lines, string AccountCode = "", Account? Details = null);
public record QuoteRevision(Guid QuoteId, Quote? Value);
public record Cash(string Category, string Detail, string Account, long Amount);
