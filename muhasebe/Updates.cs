using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace Aden;

public sealed partial class MainWindow
{
    private Border? updateCard;

    private enum UpdateState { Idle, Checking, Available, Downloading, Ready }

    private void AddUpdateSettings(Panel panel)
    {
        var releaseRepo = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "AdenReleaseRepo").Value;
        var manager = new UpdateManager(new GithubSource($"https://github.com/{releaseRepo}", null, false));
#if DEBUG
        var preview = Environment.GetEnvironmentVariable("ADEN_UPDATE_PREVIEW") == "1";
#else
        var preview = false;
#endif
        if (!preview && (!manager.IsInstalled || manager.IsPortable)) return;
        var installed = preview ? "1.0.3" : manager.CurrentVersion?.ToString() ?? "?";

        // ── Ayarlar kartı ──
        var section = new StackPanel();
        var enabled = new CheckBox
        {
            Content = "Yeni sürümleri arka planda kontrol et",
            IsChecked = store.Setting("autoUpdate", "1") == "1"
        };
        section.Children.Add(enabled);
        enabled.Checked += (_, _) => store.SaveSetting("autoUpdate", "1");
        enabled.Unchecked += (_, _) => store.SaveSetting("autoUpdate", "0");
        var description = Label($"Kurulu sürüm v{installed}", 13, Muted);
        description.Margin = new Thickness(0, SmallGap, 0, 0);
        section.Children.Add(description);
        var check = new Button();
        SetButtonContent(check, "Güncellemeleri kontrol et", "");
        var apply = new Button { Style = (Style)FindResource("PrimaryButton") };
        panel.Children.Add(SettingsCard("Güncellemeler",
            "Yeni sürüm bulunduğunda kenar çubuğunda indirme butonu çıkar. İndirme bitince yeniden başlatarak kurarsın; verilerin korunur.",
            section, check, apply));

        // ── Kenar çubuğu kartı ──
        var caption = Label("KURULU SÜRÜM", 10, Muted, true);
        var current = Label($"v{installed}", 15, Ink, true);
        current.Margin = new Thickness(0, 2, 0, 0);
        var target = Label("", 12, "#2563EB", true);
        target.Margin = new Thickness(0, 4, 0, 0);
        target.TextWrapping = TextWrapping.Wrap;
        var detail = Label("", 11, Muted);
        detail.Margin = new Thickness(0, 2, 0, 0);
        detail.TextWrapping = TextWrapping.Wrap;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(caption);
        texts.Children.Add(current);
        texts.Children.Add(target);
        texts.Children.Add(detail);

        var action = new Button
        {
            Style = (Style)FindResource("PrimaryButton"), Width = 40, Height = 40, MinHeight = 40, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(SmallGap, 0, 0, 0)
        };
        action.Template = RoundButtonTemplate();
        var spinner = Spinner();
        var slot = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(SmallGap, 0, 0, 0) };
        action.Margin = new Thickness(0);
        slot.Children.Add(action);
        slot.Children.Add(spinner);
        var row = new DockPanel();
        DockPanel.SetDock(slot, Dock.Right);
        row.Children.Add(slot);
        row.Children.Add(texts);
        var card = Card(row, new Thickness(12));
        updateCard = card;

        var state = UpdateState.Idle;
        string? newVersion = null;
        UpdateInfo? found = null;
        VelopackAsset? downloaded = preview ? null : manager.UpdatePendingRestart;
        if (downloaded is not null) { state = UpdateState.Ready; newVersion = downloaded.Version.ToString(); }
        var progress = 0;
        var message = "";

        void Present()
        {
            var highlighted = newVersion is not null && state != UpdateState.Checking || state == UpdateState.Downloading;
            card.Background = Paint(highlighted ? "#EFF4FF" : "#F9FAFB");
            card.BorderBrush = Paint(highlighted ? "#BFDBFE" : LineColor);
            target.Text = newVersion is null ? "" : $"Güncellenecek sürüm: v{newVersion}";
            target.Visibility = newVersion is null ? Visibility.Collapsed : Visibility.Visible;
            detail.Text = state switch
            {
                UpdateState.Checking => "Yeni sürüm aranıyor…",
                UpdateState.Downloading => $"İndiriliyor · %{progress}",
                UpdateState.Ready => "İndirildi · kurmak için yeniden başlat",
                _ => message
            };
            detail.Visibility = detail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            var busy = state is UpdateState.Checking or UpdateState.Downloading;
            spinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            action.Visibility = state is UpdateState.Available or UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
            slot.Visibility = busy || action.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            action.Content = Glyph(state == UpdateState.Ready ? "" : "", 16);
            var tip = state == UpdateState.Ready ? $"v{newVersion} sürümünü kurmak için yeniden başlat" : $"v{newVersion} sürümünü indir";
            action.ToolTip = tip;
            System.Windows.Automation.AutomationProperties.SetName(action, tip);

            check.IsEnabled = !busy;
            apply.Visibility = state is UpdateState.Available or UpdateState.Ready ? Visibility.Visible : Visibility.Collapsed;
            SetButtonContent(apply, state == UpdateState.Ready ? "Yeniden başlat ve güncelle" : $"v{newVersion} sürümünü indir",
                state == UpdateState.Ready ? "" : "");
            description.Text = $"Kurulu sürüm v{installed}" + (detail.Text.Length > 0 ? " · " + detail.Text : "");
        }

        action.Click += (_, _) => Proceed();
        apply.Click += (_, _) => Proceed();
        check.Click += (_, _) => _ = Check(manual: true);
        Present();

        void Proceed()
        {
            if (state == UpdateState.Available) _ = Download();
            else if (state == UpdateState.Ready) Restart();
        }

        var schedule = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        schedule.Tick += (_, _) => _ = Check(manual: false);
        Loaded += (_, _) =>
        {
            schedule.Start();
            _ = Check(manual: false);
        };
        Closed += (_, _) => schedule.Stop();

        async Task Check(bool manual)
        {
            if (state is UpdateState.Checking or UpdateState.Downloading or UpdateState.Ready ||
                closing.IsCancellationRequested || (!manual && enabled.IsChecked != true)) return;
            var previous = state;
            state = UpdateState.Checking;
            message = "";
            Present();
            try
            {
                UpdateInfo? update;
                if (preview) { await Task.Delay(2500); update = null; newVersion = "1.0.4"; }
                else update = await manager.CheckForUpdatesAsync();
                if (closing.IsCancellationRequested) return;
                if (preview) { state = UpdateState.Available; return; }
                if (update is null)
                {
                    state = UpdateState.Idle;
                    newVersion = null;
                    message = manual ? "En güncel sürümü kullanıyorsun." : "";
                    if (manual) Toast("Aden güncel. Yeni sürüm bulunamadı.");
                    return;
                }
                found = update;
                newVersion = update.TargetFullRelease.Version.ToString();
                state = UpdateState.Available;
            }
            catch (Exception error)
            {
                if (closing.IsCancellationRequested) return;
                System.Diagnostics.Trace.TraceWarning("Aden güncelleme kontrolü: {0}", error);
                state = previous == UpdateState.Available ? previous : UpdateState.Idle;
                message = "Güncelleme sunucusuna ulaşılamadı.";
                if (manual) Toast(message, true);
            }
            finally
            {
                if (!closing.IsCancellationRequested) Present();
            }
        }

        async Task Download()
        {
            if (state != UpdateState.Available || closing.IsCancellationRequested) return;
            state = UpdateState.Downloading;
            progress = 0;
            Present();
            try
            {
                if (preview)
                {
                    for (progress = 0; progress <= 100; progress += 5) { Present(); await Task.Delay(120); }
                    state = UpdateState.Ready;
                    return;
                }
                await manager.DownloadUpdatesAsync(found!, value => Dispatcher.BeginInvoke(() =>
                {
                    if (state != UpdateState.Downloading) return;
                    progress = value;
                    Present();
                }), closing.Token);
                if (closing.IsCancellationRequested) return;
                downloaded = found!.TargetFullRelease;
                state = UpdateState.Ready;
                Toast($"v{newVersion} indirildi. Kurmak için yeniden başlat.");
            }
            catch (Exception error)
            {
                if (closing.IsCancellationRequested) return;
                System.Diagnostics.Trace.TraceWarning("Aden güncelleme indirmesi: {0}", error);
                state = UpdateState.Available;
                message = "İndirme tamamlanamadı. Tekrar dene.";
                Toast(message, true);
            }
            finally
            {
                if (!closing.IsCancellationRequested) Present();
            }
        }

        void Restart()
        {
            if (state != UpdateState.Ready) return;
            if (basket.Count > 0 || payments.Count > 0 || quoteLines.Count > 0 || syncing || modals.Count > 0)
            {
                Toast("Açık satış, teklif, pencere veya eşitleme tamamlandıktan sonra yeniden başlat.", true);
                return;
            }
            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var icon = new Border
            {
                Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = Paint("#EFF4FF"),
                HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph("", 26, "#2563EB")
            };
            ((TextBlock)icon.Child).HorizontalAlignment = HorizontalAlignment.Center;
            body.Children.Add(icon);
            var title = Label("Uygulama yeniden başlatılacak", 17, bold: true);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.Margin = new Thickness(0, Gap, 0, 0);
            body.Children.Add(title);
            var text = Label($"Yeni sürümü (v{newVersion}) yüklemek için uygulamayı yeniden başlatmamız gerekiyor. Onaylıyor musunuz?",
                14, "#4B5563");
            text.TextWrapping = TextWrapping.Wrap;
            text.TextAlignment = TextAlignment.Center;
            text.Margin = new Thickness(0, SmallGap, 0, 0);
            body.Children.Add(text);
            var versions = Hstack(Pill($"v{installed}", "#F3F4F6", Muted), Glyph("", 12, Muted), Pill($"v{newVersion}", "#EFF4FF", "#1D4ED8"));
            versions.HorizontalAlignment = HorizontalAlignment.Center;
            versions.Margin = new Thickness(0, Gap, 0, 0);
            body.Children.Add(versions);
            var note = Label("Verilerin korunur; yeniden başlatma birkaç saniye sürer.", 12, Muted);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.Margin = new Thickness(0, 12, 0, 0);
            body.Children.Add(note);

            var modal = ShowModal("Güncelleme hazır", body, 440);
            Append(modal.Footer, Act("Daha sonra", modal.Close));
            var yes = Append(modal.Footer, Act("Evet, yeniden başlat", () =>
            {
                if (preview) { modal.Close(); Toast("Önizleme modu: gerçek güncelleme uygulanmadı."); return; }
                try
                {
                    manager.WaitExitThenApplyUpdates(downloaded!, silent: true, restart: true);
                }
                catch (Exception error)
                {
                    Toast($"Güncelleme başlatılamadı: {error.Message}", true);
                    return;
                }
                modal.Close();
                Close();
            }, "PrimaryButton", ""));
            modal.Footer.Loaded += (_, _) => yes.Focus();
        }
    }

    private static Border Pill(string text, string background, string color) => new()
    {
        Background = Paint(background), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 4),
        Child = Label(text, 12, color, true)
    };

    /// <summary>Yuvarlak, gölgeli mavi buton şablonu.</summary>
    private static ControlTemplate RoundButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var surface = new FrameworkElementFactory(typeof(Ellipse), "Surface");
        surface.SetValue(Shape.FillProperty, Paint("#2563EB"));
        surface.SetValue(UIElement.EffectProperty,
            new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.3, Color = Color.FromRgb(0x25, 0x63, 0xEB) });
        grid.AppendChild(surface);
        var shade = new FrameworkElementFactory(typeof(Ellipse), "Shade");
        shade.SetValue(Shape.FillProperty, Brushes.Black);
        shade.SetValue(UIElement.OpacityProperty, 0.0);
        grid.AppendChild(shade);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(TextElement.ForegroundProperty, Brushes.White);
        grid.AppendChild(content);
        template.VisualTree = grid;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.12, "Shade"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.25, "Shade"));
        template.Triggers.Add(pressed);
        return template;
    }

    /// <summary>Dönen yükleniyor göstergesi (mavi halka).</summary>
    private static FrameworkElement Spinner()
    {
        var host = new Grid { Width = 40, Height = 40 };
        host.Children.Add(new Ellipse { Fill = Paint("#EFF4FF") });
        var ring = new Grid { Width = 22, Height = 22, RenderTransformOrigin = new Point(0.5, 0.5) };
        ring.Children.Add(new Ellipse { Stroke = Paint("#BFDBFE"), StrokeThickness = 3 });
        ring.Children.Add(new Path
        {
            Stroke = Paint("#2563EB"), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 11,1.5 A 9.5,9.5 0 0 1 20.5,11")
        });
        var rotate = new RotateTransform();
        ring.RenderTransform = rotate;
        host.Children.Add(ring);
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever };
        host.IsVisibleChanged += (_, _) => rotate.BeginAnimation(RotateTransform.AngleProperty, host.IsVisible ? spin : null);
        System.Windows.Automation.AutomationProperties.SetName(host, "Yükleniyor");
        return host;
    }
}
