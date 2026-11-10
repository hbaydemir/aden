using System.Globalization;
using System.IO;
using System.Windows;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Aden;

public static class QuotePdf
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Color Ink = Color.FromRgb(20, 36, 70);
    private static readonly Color Blue = Color.FromRgb(29, 64, 175);
    private static readonly Color Pale = Color.FromRgb(235, 241, 255);

    public static void Save(string path, Guid id, DateTime date, Quote quote,
        string company = "Aden", string companyContact = "", string configuredLogo = "")
    {
        var document = new Document();
        document.Info.Title = $"Aden Satış Teklifi {id:N}";
        document.Info.Author = "Aden";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Segoe UI";
        normal.Font.Size = 10;
        normal.Font.Color = Ink;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(3.25);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.65);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.65);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.65);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(1.3);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.85);

        using var logo = new MemoryStream();
        if (string.IsNullOrWhiteSpace(configuredLogo))
        {
            var resource = Application.GetResourceStream(
                new Uri("/Aden.Desktop;component/Assets/aden-icon-text-logo.png", UriKind.Relative))
                ?? throw new InvalidOperationException("Aden logosu yüklenemedi.");
            using var logoStream = resource.Stream;
            logoStream.CopyTo(logo);
        }
        else
        {
            logo.Write(Convert.FromBase64String(configuredLogo));
        }
        logo.Position = 0;
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(logo,
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var image = section.Headers.Primary.AddImage("base64:" + Convert.ToBase64String(logo.ToArray()));
        image.Width = Unit.FromCentimeter(Math.Min(4.0, 1.35 * frame.PixelWidth / frame.PixelHeight));
        var title = section.AddParagraph("SATIŞ TEKLİFİ");
        title.Format.Font.Size = 24;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Blue;
        title.Format.SpaceAfter = Unit.FromPoint(8);
        var details = section.AddParagraph($"Teklif no {id.ToString("N")[..12].ToUpperInvariant()}  |  {date.ToString("dd.MM.yyyy", Turkish)}");
        details.Format.SpaceAfter = Unit.FromPoint(15);
        var parties = section.AddTable();
        parties.AddColumn(Unit.FromCentimeter(8.75));
        parties.AddColumn(Unit.FromCentimeter(8.75));
        var partyRow = parties.AddRow();
        AddParty(partyRow.Cells[0], "TEKLİFİ VEREN", company, companyContact);
        AddParty(partyRow.Cells[1], "MÜŞTERİ", quote.Customer, quote.Contact);
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
        var table = section.AddTable();
        table.Borders.Color = Color.FromRgb(220, 229, 243);
        table.Borders.Width = Unit.FromPoint(0.5);
        table.TopPadding = Unit.FromPoint(6);
        table.BottomPadding = Unit.FromPoint(6);
        table.AddColumn(Unit.FromCentimeter(8.4));
        table.AddColumn(Unit.FromCentimeter(1.7)).Format.Alignment = ParagraphAlignment.Right;
        table.AddColumn(Unit.FromCentimeter(3.7)).Format.Alignment = ParagraphAlignment.Right;
        table.AddColumn(Unit.FromCentimeter(3.7)).Format.Alignment = ParagraphAlignment.Right;
        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Shading.Color = Blue;
        heading.Format.Font.Color = Colors.White;
        heading.Format.Font.Bold = true;
        string[] columns = ["Ürün", "Adet", "Birim fiyat", "Tutar"];
        for (var index = 0; index < columns.Length; index++)
            heading.Cells[index].AddParagraph(columns[index]);

        for (var index = 0; index < quote.Lines.Length; index++)
        {
            var line = quote.Lines[index];
            var row = table.AddRow();
            if (index % 2 == 0) row.Shading.Color = Pale;
            row.Cells[0].AddParagraph(line.Name);
            row.Cells[1].AddParagraph(line.Quantity.ToString(Turkish));
            row.Cells[2].AddParagraph(Money(line.UnitPrice));
            row.Cells[3].AddParagraph(Money(line.UnitPrice * line.Quantity));
        }

        var total = table.AddRow();
        total.Shading.Color = Pale;
        total.Format.Font.Bold = true;
        total.Format.Font.Color = Blue;
        total.Cells[0].MergeRight = 2;
        total.Cells[0].AddParagraph("Genel toplam");
        total.Cells[3].AddParagraph(Money(quote.Lines.Sum(line => line.UnitPrice * line.Quantity)));

        var footer = section.Footers.Primary.AddParagraph("ADEN   /   SATIŞ TEKLİFİ");
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Blue;
        footer.AddTab();
        footer.Format.TabStops.AddTabStop(Unit.FromCentimeter(17.5), TabAlignment.Right);
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var target = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".aden-{Guid.NewGuid():N}.tmp");
        try
        {
            pdf.Save(temporary);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void AddParty(Cell cell, string label, string name, string contact)
    {
        var heading = cell.AddParagraph(label);
        heading.Format.Font.Color = Blue;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceAfter = Unit.FromPoint(4);
        var paragraph = cell.AddParagraph();
        paragraph.AddFormattedText(name, TextFormat.Bold);
        if (contact.Length == 0) return;
        foreach (var line in contact.Replace("\\n", "\n").ReplaceLineEndings("\n").Split('\n'))
        {
            paragraph.AddLineBreak();
            paragraph.AddText(line);
        }
    }

    private static string Money(long cents) => (cents / 100m).ToString("N2", Turkish) + " TL";
}
