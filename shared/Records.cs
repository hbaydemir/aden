namespace Aden;

public record Entry(Guid Id, string Kind, DateTimeOffset CreatedAt, string Data);
public record Change(long Sequence, Entry Entry);
public record SyncPage(long Cursor, Change[] Changes);
public record Product(string Code, string Name, string Barcode, long Price, int Minimum, bool Archived = false);
public record Account(string Code, string Name, string Contact, bool Archived = false);
public record Stock(string Code, int Warehouse, int Shop, long UnitCost, string Reference);
public record Line(string Code, string Name, int Quantity, long UnitPrice);
public record Payment(string Method, long Amount, string Reference);
public record Sale(string Account, Line[] Lines, Payment[] Payments);
public record Quote(string Customer, string Contact, Line[] Lines, string AccountCode = "");
public record QuoteRevision(Guid QuoteId, Quote? Value);
public record Cash(string Category, string Detail, string Account, long Amount);
