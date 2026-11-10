using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Aden;

public sealed partial class MainWindow
{
    private Button? updateNotice;

    private void AddUpdateSettings(StackPanel panel)
    {
        var releaseRepo = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "AdenReleaseRepo").Value;
        var manager = new UpdateManager(new GithubSource($"https://github.com/{releaseRepo}", null, false));
        if (!manager.IsInstalled || manager.IsPortable) return;

        var section = new StackPanel { Margin = new Thickness(4, 16, 4, 0) };
        panel.Children.Add(section);
        var enabled = new CheckBox
        {
            Content = "Yeni sürümleri arka planda kontrol et",
            IsChecked = store.Setting("autoUpdate", "1") == "1"
        };
        section.Children.Add(enabled);
        enabled.Checked += (_, _) => store.SaveSetting("autoUpdate", "1");
        enabled.Unchecked += (_, _) => store.SaveSetting("autoUpdate", "0");
        var description = Label($"Kurulu sürüm {manager.CurrentVersion}", 13);
        section.Children.Add(description);
        var currentVersion = description.Text;
        var busy = false;
        VelopackAsset? downloaded = manager.UpdatePendingRestart;
        var actions = Form(section);
        var check = new Button { Content = "Güncellemeleri kontrol et", Margin = new Thickness(4) };
        var apply = new Button { Content = "Güncelle ve yeniden başlat", Margin = new Thickness(4) };
        var notice = new Button { Content = "Güncelleme hazır", Margin = new Thickness(4, 0, 8, 0) };
        apply.Style = notice.Style = (Style)FindResource("PrimaryButton");
        actions.Children.Add(check);
        actions.Children.Add(apply);
        updateNotice = notice;
        check.Click += (_, _) => _ = Check(manual: true);
        apply.Click += (_, _) => Apply();
        notice.Click += (_, _) => Apply();
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
            notice.Visibility = apply.Visibility;
            if (downloaded is not null)
                description.Text = $"{currentVersion} · {downloaded.Version} indirildi";
        }

        async Task Check(bool manual)
        {
            if (busy || closing.IsCancellationRequested || (!manual && enabled.IsChecked != true)) return;
            busy = true;
            check.IsEnabled = false;
            description.Text = $"{currentVersion} · Kontrol ediliyor";
            try
            {
                var update = await manager.CheckForUpdatesAsync();
                if (closing.IsCancellationRequested) return;
                if (update is null)
                {
                    description.Text = $"{currentVersion} · Yeni sürüm bulunamadı";
                    return;
                }

                description.Text = $"{update.TargetFullRelease.Version} indiriliyor";
                await manager.DownloadUpdatesAsync(update, cancelToken: closing.Token);
                if (!closing.IsCancellationRequested) downloaded = update.TargetFullRelease;
            }
            catch (Exception error)
            {
                if (closing.IsCancellationRequested) return;
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
            if (basket.Count > 0 || payments.Count > 0 || quoteLines.Count > 0 || syncing ||
                System.Windows.Application.Current.Windows.OfType<Window>().Any(window => window.Owner == this && window.IsVisible))
            {
                MessageBox.Show(this, "Açık satış, teklif, pencere veya eşitleme tamamlandıktan sonra güncellemeyi yeniden başlat.", "Aden");
                return;
            }
            if (MessageBox.Show(this, $"{downloaded.Version} sürümüne geçmek için Aden yeniden başlatılsın mı?",
                "Aden güncellemesi", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    manager.WaitExitThenApplyUpdates(downloaded, silent: true, restart: true);
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, "Güncelleme başlatılamadı");
                    return;
                }
            }
            else
                return;
            Close();
        }
    }
}

