using System.Text.Json.Serialization;

namespace Aden;

public record Entry(Guid Id, string Kind, DateTimeOffset CreatedAt, string Data);
public record Change(long Sequence, Entry Entry);
public record SyncPage(long Cursor, Change[] Changes);
public record Product(string Code, string Name, string Barcode, long Price, int Minimum, bool Archived = false,
    string Description = "", string Category = "", long? DiscountedPrice = null, string[]? Photos = null, string Cover = "",
    int? Vat = null, bool NetPrice = false, string Unit = "Adet", string Variation = "", string[]? Options = null)
{
    // NetPrice=true: Price ve DiscountedPrice KDV hariçtir. Eski kayıtlarda (false) KDV dahildir.
    /// <summary>Sepete yansıyan fiyat: indirim varsa indirimli fiyat.</summary>
    [JsonIgnore] public long SalePrice => DiscountedPrice is long discounted && discounted < Price ? discounted : Price;
}
/// <summary>Varyasyon tipi (Renk, Beden…) ve varyantları. Varsayılan tipler sabit kimliklidir.</summary>
public record VariationType(string Id, string Name, string[] Values, bool Archived = false);
public record ProductCategory(string Id, string Name, string Parent, bool Archived = false);
public record Phone(string Kind, string Number);
public record Email(string Name, string Address);
public record Account(string Code, string Name, string Contact, bool Archived = false,
    string Type = "Kurumsal", Phone[]? Phones = null, Email[]? Emails = null, string TaxNumber = "", string TaxOffice = "",
    string BillingAddress = "", string Address = "", bool SameAddress = false, string Website = "");
/// <summary>Stok hareketi. Mal kabulde Account tedarikçi carisidir; Status: Ödendi, Borç, Parçalı, Vadeli.
/// Paid: Parçalı ödemede peşin ödenen tutar. Term: Vadeli alımda ay sayısı. Type "Sayım": stok düzeltmesi.</summary>
public record Stock(string Code, int Warehouse, int Shop, long UnitCost, string Reference, int? Vat = null, long? NetCost = null,
    string Account = "", string Supplier = "", string Status = "", string Method = "", long Paid = 0, int Term = 0, string Type = "");
public record StockRevision(Guid StockId, Stock? Value);
/// <summary>UnitPrice: KDV dahil, iskonto sonrası birim fiyat. Net: iskonto öncesi KDV hariç birim fiyat.
/// Discount: Net'e uygulanan iskonto yüzdesi. Eski kayıtlarda Vat/Net boştur.</summary>
public record Line(string Code, string Name, int Quantity, long UnitPrice, int? Vat = null, long? Net = null, decimal Discount = 0);
public record Payment(string Method, long Amount, string Reference);
public record Sale(string Account, Line[] Lines, Payment[] Payments);
/// <summary>Satış düzenleme (Value) veya silme (Value = null).</summary>
public record SaleRevision(Guid SaleId, Sale? Value, string Reason = "");
/// <summary>Satış iptali, iadesi veya değişimi. Returned: geri alınan, Given: yerine verilen ürünler.
/// Payments toplamı = Given − Returned; eksi tutar müşteriye geri ödemedir.</summary>
public record SaleReturn(Guid SaleId, string Type, Line[] Returned, Line[] Given, Payment[] Payments, string Reason = "");
public record ReturnLine(string Code, string Name, int Quantity, long UnitCost);
/// <summary>Tedarikçiye hatalı/sorunlu ürün iadesi. Source: Depo veya Dükkân.</summary>
public record SupplierReturn(string Number, string Account, string Supplier, ReturnLine[] Lines, string Source, string Reason);
public record Quote(string Customer, string Contact, Line[] Lines, string AccountCode = "", Account? Details = null);
public record QuoteRevision(Guid QuoteId, Quote? Value);
public record Cash(string Category, string Detail, string Account, long Amount);
