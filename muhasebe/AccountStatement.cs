using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace Aden;

/// <summary>Cari hareketleri (ekstre): alacak ve borç kalemleri, yürüyen bakiye ve PDF çıktısı.</summary>
public sealed partial class MainWindow
{
    private sealed record StatementRow(DateTime Tarih, string İşlem, string Açıklama, string Alacak, string Borç, string Bakiye);

    private string BalanceText(long balance) => balance > 0 ? $"{Format(balance)} (A)" : balance < 0 ? $"{Format(-balance)} (B)" : Format(0);

    private void AccountStatement(Account account)
    {
        var all = AccountMoves(account.Code);
        var start = new DatePicker { SelectedDate = all.Count > 0 ? all[0].Date.Date : DateTime.Today.AddMonths(-1), Width = 150 };
        var end = new DatePicker { SelectedDate = DateTime.Today, Width = 150 };
        var body = new DockPanel();
        var bar = Hstack(Label("Tarih aralığı", 13, Muted, true), start, Label("–", 13, Muted), end);
        foreach (var child in bar.Children.OfType<TextBlock>()) child.VerticalAlignment = VerticalAlignment.Center;
        bar.Margin = new Thickness(0, 0, 0, Gap);
        DockPanel.SetDock(bar, Dock.Top);
        body.Children.Add(bar);

        var totals = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, Gap, -SmallGap, 0) };
        DockPanel.SetDock(totals, Dock.Bottom);
        body.Children.Add(totals);
        TextBlock Stat(string caption, string color)
        {
            var box = new StackPanel();
            box.Children.Add(Label(caption, 12, Muted, true));
            var value = Label("", 17, color, true);
            value.Margin = new Thickness(0, 4, 0, 0);
            box.Children.Add(value);
            totals.Children.Add(new Border
            {
                Child = box, Background = Paint("#F9FAFB"), CornerRadius = new CornerRadius(10), BorderBrush = Paint(LineColor),
                BorderThickness = new Thickness(1), Padding = new Thickness(12), Margin = new Thickness(0, 0, SmallGap, 0)
            });
            return value;
        }
        var openingStat = Stat("Devreden bakiye", Ink);
        var debitStat = Stat("Toplam alacak", "#15803D");
        var creditStat = Stat("Toplam borç", "#DC2626");
        var balanceStat = Stat("Bakiye", "#1D4ED8");

        var grid = Table();
        grid.AutoGenerateColumns = false;
        grid.Height = 380;
        foreach (var (title, path, weight) in new[]
        {
            ("Tarih", nameof(StatementRow.Tarih), 0.9), ("İşlem", nameof(StatementRow.İşlem), 0.9), ("Açıklama", nameof(StatementRow.Açıklama), 2.0),
            ("Alacak", nameof(StatementRow.Alacak), 0.8), ("Borç", nameof(StatementRow.Borç), 0.8), ("Bakiye", nameof(StatementRow.Bakiye), 0.9)
        })
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = title, Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
                Binding = new Binding(path) { StringFormat = path == nameof(StatementRow.Tarih) ? "dd.MM.yyyy HH:mm" : null }
            });
        body.Children.Add(Card(grid));

        (long Opening, List<AccountMove> Moves) Range()
        {
            var from = start.SelectedDate ?? DateTime.MinValue;
            var to = (end.SelectedDate ?? DateTime.MaxValue.Date).AddDays(1);
            if (to <= from) throw new InvalidOperationException("Geçerli bir tarih aralığı seç.");
            return (all.Where(m => m.Date < from).Sum(m => m.Debit - m.Credit), all.Where(m => m.Date >= from && m.Date < to).ToList());
        }
        List<StatementRow> Rows(long opening, List<AccountMove> moves)
        {
            var running = opening;
            var rows = new List<StatementRow>();
            foreach (var move in moves)
            {
                running += move.Debit - move.Credit;
                rows.Add(new StatementRow(move.Date, move.Kind, move.Detail, move.Debit > 0 ? Format(move.Debit) : "",
                    move.Credit > 0 ? Format(move.Credit) : "", BalanceText(running)));
            }
            return rows;
        }
        void Render()
        {
            try
            {
                var (opening, moves) = Range();
                grid.ItemsSource = Rows(opening, moves);
                openingStat.Text = BalanceText(opening);
                debitStat.Text = Format(moves.Sum(m => m.Debit));
                creditStat.Text = Format(moves.Sum(m => m.Credit));
                var balance = opening + moves.Sum(m => m.Debit - m.Credit);
                balanceStat.Text = balance > 0 ? $"Alacağımız {Format(balance)}" : balance < 0 ? $"Borcumuz {Format(-balance)}" : Format(0);
                balanceStat.Foreground = Paint(balance > 0 ? "#15803D" : balance < 0 ? "#DC2626" : Ink);
            }
            catch (InvalidOperationException) { grid.ItemsSource = null; }
        }
        start.SelectedDateChanged += (_, _) => Render();
        end.SelectedDateChanged += (_, _) => Render();
        Render();

        var modal = ShowModal($"Cari hareketleri · {account.Name}", body, 1040,
            subtitle: $"{account.Code} · {account.Type} · Alacak (A): cari bize borçlu · Borç (B): biz cariye borçluyuz");
        Append(modal.Footer, Act("Kapat", modal.Close));
        Append(modal.Footer, Act("PDF olarak dışa aktar", () =>
        {
            var (opening, moves) = Range();
            var dialog = new SaveFileDialog
            {
                Title = "Cari ekstresini kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true,
                FileName = $"Aden-Cari-{account.Code}-{DateTime.Today:yyyyMMdd}"
            };
            if (dialog.ShowDialog(this) != true) return;
            var rows = Rows(opening, moves);
            var balance = opening + moves.Sum(m => m.Debit - m.Credit);
            ReportPdf.Save(dialog.FileName, "CARİ HESAP EKSTRESİ", Company(),
                [
                    ("Cari", $"{account.Name} ({account.Code})"), ("Cari tipi", account.Type),
                    ("Dönem", $"{start.SelectedDate:dd.MM.yyyy} – {end.SelectedDate:dd.MM.yyyy}"),
                    ("Devreden bakiye", BalanceText(opening))
                ],
                ContactSummary(account),
                ["Tarih", "İşlem", "Açıklama", "Alacak", "Borç", "Bakiye"], [0.14, 0.15, 0.33, 0.12, 0.12, 0.14],
                rows.Select(r => new[] { r.Tarih.ToString("dd.MM.yyyy", culture), r.İşlem, r.Açıklama, r.Alacak, r.Borç, r.Bakiye }).ToArray(),
                [
                    ("Toplam alacak", Format(moves.Sum(m => m.Debit))), ("Toplam borç", Format(moves.Sum(m => m.Credit))),
                    (balance >= 0 ? "Bakiye (alacağımız)" : "Bakiye (borcumuz)", Format(Math.Abs(balance)))
                ],
                "A: Alacak bakiyesi (cari bize borçlu) · B: Borç bakiyesi (biz cariye borçluyuz). Bu ekstre bilgi amaçlıdır.", leftColumns: 3);
            Toast("Cari ekstresi PDF olarak kaydedildi.");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true }); } catch { }
        }, "PrimaryButton", ""));
    }
}
