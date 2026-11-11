using System.IO;
using System.Windows;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Aden;

/// <summary>Tablo tabanlı belgeler (cari ekstresi, tedarikçi iade formu) için ortak A4 PDF çıktısı.</summary>
public static class ReportPdf
{
    private static readonly Color Ink = Color.FromRgb(20, 36, 70);
    private static readonly Color Blue = Color.FromRgb(29, 64, 175);
    private static readonly Color Pale = Color.FromRgb(235, 241, 255);
    private static readonly Color Line = Color.FromRgb(226, 232, 240);
    private static readonly Color Muted = Color.FromRgb(100, 116, 139);

    public static void Save(string path, string title, (string Name, string Contact, string Logo) company,
        (string Label, string Value)[] meta, string partyContact, string[] headers, double[] widths, string[][] rows,
        (string Label, string Value)[] totals, string note = "", bool signatures = false, int leftColumns = 2)
    {
        var document = new Document();
        document.Info.Title = title;
        document.Info.Author = company.Name;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Segoe UI";
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(3.1);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.HeaderDistance = Unit.FromCentimeter(1.2);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(0.8);

        AddLogo(section, company.Logo);
        var heading = section.Headers.Primary.AddParagraph(company.Name);
        heading.Format.Alignment = ParagraphAlignment.Right;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 11;
        heading.Format.SpaceBefore = Unit.FromCentimeter(-1.1);
        if (company.Contact.Length > 0)
        {
            var contact = section.Headers.Primary.AddParagraph(company.Contact.ReplaceLineEndings(" · "));
            contact.Format.Alignment = ParagraphAlignment.Right;
            contact.Format.Font.Size = 8;
            contact.Format.Font.Color = Muted;
        }
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Muted;
        footer.AddText($"{company.Name} · {DateTime.Now:dd.MM.yyyy HH:mm} · Sayfa ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        var titleParagraph = section.AddParagraph(title);
        titleParagraph.Format.Font.Size = 20;
        titleParagraph.Format.Font.Bold = true;
        titleParagraph.Format.Font.Color = Blue;
        titleParagraph.Format.SpaceAfter = Unit.FromPoint(10);

        var info = section.AddTable();
        info.Borders.Visible = false;
        info.AddColumn(Unit.FromCentimeter(8.9));
        info.AddColumn(Unit.FromCentimeter(8.9));
        var infoRow = info.AddRow();
        var left = infoRow.Cells[0];
        foreach (var (label, value) in meta)
        {
            var paragraph = left.AddParagraph();
            paragraph.AddFormattedText(label + ": ", TextFormat.Bold);
            paragraph.AddText(value);
            paragraph.Format.SpaceAfter = Unit.FromPoint(2);
        }
        if (partyContact.Length > 0)
        {
            var right = infoRow.Cells[1].AddParagraph(partyContact.ReplaceLineEndings("\n"));
            right.Format.Font.Size = 8.5;
            right.Format.Font.Color = Muted;
        }
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

        var table = section.AddTable();
        table.Borders.Color = Line;
        table.Borders.Width = 0.5;
        table.LeftPadding = Unit.FromPoint(5);
        table.RightPadding = Unit.FromPoint(5);
        table.TopPadding = Unit.FromPoint(4);
        table.BottomPadding = Unit.FromPoint(4);
        var width = 17.8;
        for (var i = 0; i < headers.Length; i++)
        {
            var column = table.AddColumn(Unit.FromCentimeter(width * widths[i]));
            column.Format.Alignment = i < leftColumns ? ParagraphAlignment.Left : ParagraphAlignment.Right;
        }
        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = Blue;
        head.Format.Font.Color = Colors.White;
        head.Format.Font.Bold = true;
        for (var i = 0; i < headers.Length; i++) head.Cells[i].AddParagraph(headers[i]);
        for (var r = 0; r < rows.Length; r++)
        {
            var row = table.AddRow();
            if (r % 2 == 1) row.Shading.Color = Color.FromRgb(248, 250, 252);
            for (var i = 0; i < headers.Length && i < rows[r].Length; i++) row.Cells[i].AddParagraph(rows[r][i]);
        }
        if (rows.Length == 0)
        {
            var row = table.AddRow();
            row.Cells[0].MergeRight = headers.Length - 1;
            row.Cells[0].AddParagraph("Kayıt yok.").Format.Font.Color = Muted;
        }

        if (totals.Length > 0)
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(6);
            var summary = section.AddTable();
            summary.Borders.Visible = false;
            summary.AddColumn(Unit.FromCentimeter(9.8));
            summary.AddColumn(Unit.FromCentimeter(5));
            summary.AddColumn(Unit.FromCentimeter(3));
            for (var i = 0; i < totals.Length; i++)
            {
                var row = summary.AddRow();
                var last = i == totals.Length - 1;
                row.Cells[1].AddParagraph(totals[i].Label).Format.Font.Bold = last;
                var value = row.Cells[2].AddParagraph(totals[i].Value);
                value.Format.Alignment = ParagraphAlignment.Right;
                value.Format.Font.Bold = true;
                if (last)
                {
                    row.Cells[1].Shading.Color = Pale;
                    row.Cells[2].Shading.Color = Pale;
                    value.Format.Font.Color = Blue;
                }
                row.TopPadding = Unit.FromPoint(3);
                row.BottomPadding = Unit.FromPoint(3);
            }
        }
        if (note.Length > 0)
        {
            var paragraph = section.AddParagraph(note);
            paragraph.Format.SpaceBefore = Unit.FromPoint(14);
            paragraph.Format.Font.Size = 9;
        }
        if (signatures)
        {
            section.AddParagraph().Format.SpaceAfter = Unit.FromCentimeter(1.2);
            var signs = section.AddTable();
            signs.Borders.Visible = false;
            signs.AddColumn(Unit.FromCentimeter(8.9));
            signs.AddColumn(Unit.FromCentimeter(8.9));
            var row = signs.AddRow();
            foreach (var (cell, text) in new[] { (row.Cells[0], "Teslim eden"), (row.Cells[1], "Teslim alan (tedarikçi)") })
            {
                var paragraph = cell.AddParagraph(text);
                paragraph.Format.Alignment = ParagraphAlignment.Center;
                paragraph.Format.Font.Bold = true;
                var line = cell.AddParagraph("\n\n________________________\nAd soyad · İmza");
                line.Format.Alignment = ParagraphAlignment.Center;
                line.Format.Font.Color = Muted;
            }
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PdfDocument.Save(path);
    }

    private static void AddLogo(Section section, string configured)
    {
        using var logo = new MemoryStream();
        if (string.IsNullOrWhiteSpace(configured))
        {
            var resource = Application.GetResourceStream(new Uri("/Aden.Desktop;component/Assets/aden-icon-text-logo.png", UriKind.Relative));
            if (resource is null) return;
            using var stream = resource.Stream;
            stream.CopyTo(logo);
        }
        else logo.Write(Convert.FromBase64String(configured));
        logo.Position = 0;
        var frame = System.Windows.Media.Imaging.BitmapDecoder.Create(logo, System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0];
        var image = section.Headers.Primary.AddImage("base64:" + Convert.ToBase64String(logo.ToArray()));
        image.Width = Unit.FromCentimeter(Math.Min(4.0, 1.25 * frame.PixelWidth / frame.PixelHeight));
    }
}
