using ClosedXML.Excel;

namespace Aden;

public static class SalesWorkbook
{
    public static void Save(string path, IEnumerable<(Entry Entry, Sale Value)> sales)
    {
        using var book = new XLWorkbook();
        var summary = book.AddWorksheet("Satışlar");
        var items = book.AddWorksheet("Ürün kalemleri");
        var payments = book.AddWorksheet("Ödemeler");
        Header(summary, "Satış no", "Tarih", "Cari", "Toplam (TL)");
        Header(items, "Satış no", "Ürün kodu", "Ürün", "Adet", "Birim fiyat (TL)", "Tutar (TL)");
        Header(payments, "Satış no", "Yöntem", "Tutar (TL)", "Referans");

        int saleRow = 2, itemRow = 2, paymentRow = 2;
        foreach (var (entry, sale) in sales)
        {
            var id = entry.Id.ToString();
            summary.Cell(saleRow, 1).Value = id;
            summary.Cell(saleRow, 2).Value = entry.CreatedAt.LocalDateTime;
            summary.Cell(saleRow, 3).Value = sale.Account;
            summary.Cell(saleRow++, 4).Value = sale.Lines.Sum(line => line.Quantity * line.UnitPrice) / 100m;
            foreach (var line in sale.Lines)
            {
                items.Cell(itemRow, 1).Value = id;
                items.Cell(itemRow, 2).Value = line.Code;
                items.Cell(itemRow, 3).Value = line.Name;
                items.Cell(itemRow, 4).Value = line.Quantity;
                items.Cell(itemRow, 5).Value = line.UnitPrice / 100m;
                items.Cell(itemRow++, 6).Value = line.Quantity * line.UnitPrice / 100m;
            }
            foreach (var payment in sale.Payments)
            {
                payments.Cell(paymentRow, 1).Value = id;
                payments.Cell(paymentRow, 2).Value = payment.Method;
                payments.Cell(paymentRow, 3).Value = payment.Amount / 100m;
                payments.Cell(paymentRow++, 4).Value = payment.Reference;
            }
        }
        summary.Column(2).Style.DateFormat.Format = "dd.mm.yyyy hh:mm";
        summary.Column(4).Style.NumberFormat.Format = "#,##0.00";
        items.Columns(5, 6).Style.NumberFormat.Format = "#,##0.00";
        payments.Column(3).Style.NumberFormat.Format = "#,##0.00";
        foreach (var sheet in book.Worksheets)
        {
            sheet.ColumnsUsed().AdjustToContents(12, 52);
            sheet.RangeUsed()!.SetAutoFilter();
            sheet.SheetView.FreezeRows(1);
        }
        book.SaveAs(path);
    }

    private static void Header(IXLWorksheet sheet, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++) sheet.Cell(1, i + 1).Value = titles[i];
        var header = sheet.Range(1, 1, 1, titles.Length).Style;
        header.Font.Bold = true;
        header.Font.FontColor = XLColor.White;
        header.Fill.BackgroundColor = XLColor.FromHtml("#1D40AF");
    }
}
