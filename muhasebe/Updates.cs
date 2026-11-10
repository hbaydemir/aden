using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Aden;

public sealed partial class MainWindow
{
    private Border? updateCard;

    private void AddUpdateSettings(Panel panel)
    {
        var releaseRepo = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "AdenReleaseRepo").Value;
        var manager = new UpdateManager(new GithubSource($"https://github.com/{releaseRepo}", null, false));
        if (!manager.IsInstalled || manager.IsPortable) return;

        var section = new StackPanel();
        var enabled = new CheckBox
        {
            Content = "Yeni sürümleri arka planda kontrol et",
            IsChecked = store.Setting("autoUpdate", "1") == "1"
        };
        section.Children.Add(enabled);
        enabled.Checked += (_, _) => store.SaveSetting("autoUpdate", "1");
        enabled.Unchecked += (_, _) => store.SaveSetting("autoUpdate", "0");
        var description = Label($"Kurulu sürüm {manager.CurrentVersion}", 13, Muted);
        description.Margin = new Thickness(0, SmallGap, 0, 0);
        section.Children.Add(description);
        var currentVersion = description.Text;
        var busy = false;
        VelopackAsset? downloaded = manager.UpdatePendingRestart;
        var check = new Button();
        SetButtonContent(check, "Güncellemeleri kontrol et", "");
        var apply = new Button { Style = (Style)FindResource("SuccessButton") };
        SetButtonContent(apply, "Güncelle ve yeniden başlat", "");
        panel.Children.Add(SettingsCard("Güncellemeler", "Yeni sürüm arka planda indirilir, onay verdiğinde uygulanır. Verilerin korunur.",
            section, check, apply));
        var cardContent = new StackPanel();
        var cardTitle = Label("SÜRÜM", 10, Muted, true);
        var cardVersion = Label("", 15, Ink, true);
        cardVersion.Margin = new Thickness(0, 2, 0, 0);
        var cardButton = new Button { Margin = new Thickness(0, SmallGap, 0, 0), Style = (Style)FindResource("PrimaryButton") };
        SetButtonContent(cardButton, "Güncelle", "");
        cardContent.Children.Add(cardTitle);
        cardContent.Children.Add(cardVersion);
        cardContent.Children.Add(cardButton);
        var card = Card(cardContent, new Thickness(12));
        updateCard = card;
        var newVersion = downloaded?.Version.ToString();
        check.Click += (_, _) => _ = Check(manual: true);
        apply.Click += (_, _) => Apply();
        cardButton.Click += (_, _) =>
        {
            if (downloaded is not null) Apply();
            else _ = Check(manual: true);
        };
        Present();

        var schedule = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        schedule.Tick += (_, _) => _ = Check(manual: false);
        Loaded += (_, _) =>
        {
            schedule.Start();
            _ = Check(manual: false);
        };
        Closed += (_, _) => schedule.Stop();

        void Present()
        {
            apply.Visibility = downloaded is null ? Visibility.Collapsed : Visibility.Visible;
            var highlighted = newVersion is not null;
            cardTitle.Text = highlighted ? "YENİ GÜNCELLEME" : "KURULU SÜRÜM";
            cardTitle.Foreground = Paint(highlighted ? "#2563EB" : Muted);
            cardVersion.Text = $"v{newVersion ?? manager.CurrentVersion?.ToString()}";
            cardVersion.Foreground = Paint(highlighted ? "#1D4ED8" : Ink);
            card.Background = Paint(highlighted ? "#EFF4FF" : "#F9FAFB");
            card.BorderBrush = Paint(highlighted ? "#93C5FD" : LineColor);
            cardButton.Visibility = highlighted ? Visibility.Visible : Visibility.Collapsed;
            cardButton.IsEnabled = !busy;
            SetButtonContent(cardButton, busy ? "İndiriliyor…" : "Güncelle", busy ? null : "");
            if (downloaded is not null)
                description.Text = $"{currentVersion} · {downloaded.Version} indirildi";
        }

        async Task Check(bool manual)
        {
            if (busy || closing.IsCancellationRequested || (!manual && enabled.IsChecked != true)) return;
            busy = true;
            check.IsEnabled = false;
            Present();
            description.Text = $"{currentVersion} · Kontrol ediliyor";
            try
            {
                var update = await manager.CheckForUpdatesAsync();
                if (closing.IsCancellationRequested) return;
                if (update is null)
                {
                    newVersion = downloaded?.Version.ToString();
                    description.Text = $"{currentVersion} · Yeni sürüm bulunamadı";
                    return;
                }

                newVersion = update.TargetFullRelease.Version.ToString();
                description.Text = $"{newVersion} indiriliyor";
                Present();
                await manager.DownloadUpdatesAsync(update, cancelToken: closing.Token);
                if (!closing.IsCancellationRequested) downloaded = update.TargetFullRelease;
            }
            catch (Exception error)
            {
                if (closing.IsCancellationRequested) return;
                if (downloaded is not null) newVersion = downloaded.Version.ToString();
                System.Diagnostics.Trace.TraceWarning("Aden güncelleme kontrolü: {0}", error);
                description.Text = $"{currentVersion} · Güncelleme sunucusuna ulaşılamadı veya indirme tamamlanamadı";
            }
            finally
            {
                busy = false;
                check.IsEnabled = true;
                if (!closing.IsCancellationRequested) Present();
            }
        }

        void Apply()
        {
            if (downloaded is null || busy) return;
            if (basket.Count > 0 || payments.Count > 0 || quoteLines.Count > 0 || syncing || modals.Count > 0 ||
                System.Windows.Application.Current.Windows.OfType<Window>().Any(window => window.Owner == this && window.IsVisible))
            {
                Toast("Açık satış, teklif, pencere veya eşitleme tamamlandıktan sonra güncellemeyi yeniden başlat.", true);
                return;
            }
            var target = downloaded;
            Confirm("Aden güncellemesi", $"{target.Version} sürümüne geçmek için Aden kapatılıp yeniden başlatılacak. Verilerin korunur.",
                "Güncelle ve yeniden başlat", () =>
                {
                    try
                    {
                        manager.WaitExitThenApplyUpdates(target, silent: true, restart: true);
                    }
                    catch (Exception error)
                    {
                        Toast($"Güncelleme başlatılamadı: {error.Message}", true);
                        return;
                    }
                    Close();
                }, danger: false);
        }
    }
}
