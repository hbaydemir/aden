using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Aden;

public sealed partial class MainWindow
{
    private UIElement? developersPage;
    private bool developerUnlocked;
    private Action? refreshDevices;

    // ───────────── Bağlantı ayarları ─────────────

    private void AddConnectionSettings(Panel panel)
    {
        var mode = store.Setting("mode") is { Length: > 0 } m ? m : "local";

        // Bağımsız
        var standalone = new StackPanel();
        var standaloneText = Label("Veriler yalnızca bu bilgisayarda tutulur, ağ isteği gönderilmez. Tek kasalı kullanım için.", 13, "#374151");
        standaloneText.TextWrapping = TextWrapping.Wrap;
        standalone.Children.Add(standaloneText);
        var standaloneRow = Hstack(Act("Bağımsız moda geç", () => Confirm("Bağımsız mod",
            "Bu cihaz sunucu bağlantısını kesip yalnızca kendi veritabanını kullanacak." +
            (mode == "hub" ? " Merkezi sunucu yayını durdurulur ve bağlı cihazlar eşitleyemez." : "") + " Tekrar giriş yapman gerekecek.",
            "Geç", () => _ = RunAsync(async () =>
            {
                await hub.StopAsync();
                store.SaveConnection("local", "", new Dictionary<string, string>());
                ConnectionChanged("Bağımsız mod");
            })), "PrimaryButton", ""));
        standaloneRow.Margin = new Thickness(0, Gap, 0, 0);
        standaloneRow.HorizontalAlignment = HorizontalAlignment.Right;
        standalone.Children.Add(standaloneRow);

        // Yerel sunucu
        var local = new StackPanel();
        var asHub = new RadioButton { Content = "Bu cihazı merkezi sunucu olarak ayarla", GroupName = "lan", FontSize = 14, IsChecked = mode != "client" };
        var asClient = new RadioButton { Content = "Merkezi sunucuya bağlan", GroupName = "lan", FontSize = 14, Margin = new Thickness(Gap * 1.5, 0, 0, 0), IsChecked = mode == "client" };
        local.Children.Add(Hstack(asHub, asClient));
        var hubForm = HubForm();
        var clientForm = ClientForm();
        hubForm.Margin = clientForm.Margin = new Thickness(0, Gap, 0, 0);
        local.Children.Add(hubForm);
        local.Children.Add(clientForm);
        void ToggleLan()
        {
            hubForm.Visibility = asHub.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            clientForm.Visibility = asClient.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
        asHub.Checked += (_, _) => ToggleLan();
        asClient.Checked += (_, _) => ToggleLan();
        ToggleLan();

        // Uzak sunucu
        var remote = new StackPanel();
        var url = Text(store.Setting("url", "https://"));
        var key = new PasswordBox { Password = store.Setting("key") };
        remote.Children.Add(Form(2, F(Field("Sunucu adresi", url)), F(Field("Eşitleme anahtarı", key))));
        var remoteRow = Hstack(Act("Uzak sunucuya bağlan", async () =>
        {
            if (syncing) throw new InvalidOperationException("Eşitleme tamamlanınca tekrar dene.");
            var endpoint = url.Text.Trim().TrimEnd('/');
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) ||
                uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 || key.Password.Length < 24)
                throw new InvalidOperationException("HTTPS adresi veya yerel HTTP adresi ve en az 24 karakter anahtar gir.");
            await hub.StopAsync();
            store.SaveConnection("sync", endpoint, new Dictionary<string, string> { ["url"] = endpoint, ["key"] = key.Password });
            ConnectionChanged("Uzak sunucu");
        }, "PrimaryButton", ""));
        remoteRow.Margin = new Thickness(0, Gap, 0, 0);
        remoteRow.HorizontalAlignment = HorizontalAlignment.Right;
        remote.Children.Add(remoteRow);

        var current = Label("Şu anki bağlantı: " + Connection.Describe(store), 13, "#1D4ED8", true);
        current.TextWrapping = TextWrapping.Wrap;
        current.Margin = new Thickness(0, 0, 0, Gap);
        var content = new StackPanel();
        content.Children.Add(current);
        content.Children.Add(Segments(out var select, ("Yerel Sunucu Ayarları", local), ("Uzak Sunucu Ayarları", remote), ("Bağımsız", standalone)));
        select(mode == "sync" ? 1 : mode is "hub" or "client" ? 0 : 2);
        panel.Children.Add(SettingsCard("Sunucu bağlantısı",
            "Yerel ağda bir bilgisayarı merkezi sunucu yapıp diğer kasaları ona bağlayabilir ya da internetteki uzak sunucuyu kullanabilirsin.",
            content));
        if (mode == "hub") panel.Children.Add(DevicesCard());
    }

    /// <summary>Bağlantı değişince kullanıcılar başka kaynaktan doğrulanacağı için yeniden giriş istenir.</summary>
    private void ConnectionChanged(string name)
    {
        store.Log("Sistem", session.Username, Environment.MachineName, "yerel", "Bağlantı modu değişti", Connection.Describe(store));
        Confirm("Bağlantı kaydedildi", $"{name} ayarı kaydedildi. Kullanıcı bilgileri yeni bağlantıdan doğrulanacağı için tekrar giriş yap.",
            "Giriş ekranına dön", Logout, false);
    }

    private FrameworkElement HubForm()
    {
        var stack = new StackPanel();
        var configured = store.Setting("hubHash").Length > 0;
        var name = Text(store.Setting("hubName", Environment.MachineName));
        name.MaxLength = 60;
        var address = new ComboBox { IsEditable = true, Text = store.Setting("hubAddress") };
        foreach (var ip in Hub.LocalAddresses()) address.Items.Add(ip);
        address.Items.Add("0.0.0.0");
        address.Items.Add("127.0.0.1");
        if (address.Text.Length == 0) address.Text = Hub.LocalAddresses().FirstOrDefault() ?? "0.0.0.0";
        var port = Text(store.Setting("hubPort", Hub.DefaultPort.ToString(CultureInfo.InvariantCulture)));
        var user = Text(store.Setting("hubUser", "sunucu"));
        var password = new PasswordBox();
        var repeat = new PasswordBox();
        stack.Children.Add(Form(2,
            F(Field("Sunucu adı (ağda görünen)", name), 2),
            F(Field("Sabit iç IP adresi", address, true)), F(Field("Port", port, true)),
            F(Field("Sunucu kullanıcı adı", user, true)),
            F(Field(configured ? "Sunucu şifresi (değiştirmek için doldur)" : "Sunucu şifresi", password, !configured)),
            F(Field("Sunucu şifresi (tekrar)", repeat, !configured))));
        var hint = Label("Router'da bu bilgisayara verdiğin sabit iç IP'yi seç. 0.0.0.0 tüm ağ kartlarından yayın yapar. " +
            "Sunucu kullanıcı adı ve şifresi, diğer cihazların bu sunucuya bağlanması içindir; uygulama kullanıcılarından ayrıdır.", 12, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, SmallGap, 0, 0);
        stack.Children.Add(hint);

        var state = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, Gap, 0, 0) };
        void ShowState()
        {
            if (store.Setting("mode") != "hub") { state.Text = "Bu cihaz şu an merkezi sunucu değil."; state.Foreground = Paint(Muted); return; }
            state.Text = hub.Running ? $"● Yayında: {DisplayAddress()}:{hub.Port}  ·  Ağ keşfi UDP {Hub.DiscoveryPort}" : "● Kapalı: " + (hub.Error ?? "başlatılmadı");
            state.Foreground = Paint(hub.Running ? "#16A34A" : "#DC2626");
        }
        ShowState();
        stack.Children.Add(state);

        async Task Publish()
        {
            if (!System.Net.IPAddress.TryParse(address.Text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new InvalidOperationException("Geçerli bir IPv4 adresi gir (ör. 192.168.1.10).");
            if (!int.TryParse(port.Text.Trim(), out var portNumber) || portNumber is < 1024 or > 65535)
                throw new InvalidOperationException("Port 1024 ile 65535 arasında olmalı.");
            var hubUser = Users.Normalize(user.Text);
            if (!Users.ValidUsername(hubUser)) throw new InvalidOperationException("Sunucu kullanıcı adı 3-32 karakter olmalı (harf, rakam, . _ -).");
            if (password.Password.Length > 0 || !configured)
            {
                if (password.Password.Length < 6) throw new InvalidOperationException("Sunucu şifresi en az 6 karakter olmalı.");
                if (password.Password != repeat.Password) throw new InvalidOperationException("Sunucu şifreleri aynı değil.");
            }
            if (name.Text.Trim().Length == 0) throw new InvalidOperationException("Sunucu adı boş olamaz.");
            var values = new Dictionary<string, string>
            {
                ["hubName"] = name.Text.Trim(), ["hubAddress"] = ip.ToString(),
                ["hubPort"] = portNumber.ToString(CultureInfo.InvariantCulture), ["hubUser"] = hubUser
            };
            if (store.Setting("hubId").Length == 0) values["hubId"] = Guid.NewGuid().ToString("N");
            if (password.Password.Length > 0)
            {
                var (hash, salt) = Users.HashSecret(password.Password);
                values["hubHash"] = hash;
                values["hubSalt"] = salt;
            }
            var wasHub = store.Setting("mode") == "hub";
            store.SaveConnection("hub", "", values);
            await hub.StartAsync();
            ShowState();
            if (!hub.Running) throw new InvalidOperationException(hub.Error ?? "Merkezi sunucu başlatılamadı.");
            store.Log("Sistem", session.Username, Environment.MachineName, "yerel", "Merkezi sunucu ayarlandı", $"{ip}:{portNumber} · {hubUser}");
            if (wasHub) Toast($"Merkezi sunucu güncellendi · {DisplayAddress()}:{hub.Port}");
            else ConnectionChanged("Merkezi sunucu");
        }
        var buttons = Hstack(
            Act("Yayını durdur", async () =>
            {
                if (!hub.Running) throw new InvalidOperationException("Merkezi sunucu zaten kapalı.");
                await hub.StopAsync();
                ShowState();
                Toast("Merkezi sunucu durduruldu. Bağlı cihazlar yeniden başlatılana kadar eşitleyemez.");
            }, "DangerButton", ""),
            Act("Güvenlik duvarı izni", () =>
            {
                // Windows Güvenlik Duvarı'nda bu port ve keşif için gelen bağlantı izni (yönetici onayı istenir).
                var portText = port.Text.Trim();
                if (!int.TryParse(portText, out _)) throw new InvalidOperationException("Önce geçerli bir port gir.");
                var command = $"netsh advfirewall firewall delete rule name=\"Aden Merkezi Sunucu\" & " +
                    $"netsh advfirewall firewall add rule name=\"Aden Merkezi Sunucu\" dir=in action=allow protocol=TCP localport={portText} profile=private,domain & " +
                    $"netsh advfirewall firewall add rule name=\"Aden Merkezi Sunucu\" dir=in action=allow protocol=UDP localport={Hub.DiscoveryPort} profile=private,domain";
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + command)
                        { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden });
                    Toast("Güvenlik duvarı kuralı eklendi (özel ağlar).");
                }
                catch (System.ComponentModel.Win32Exception) { Toast("Yönetici onayı verilmedi.", true); }
            }, null, ""),
            Act(store.Setting("mode") == "hub" ? "Kaydet ve yeniden başlat" : "Merkezi sunucu yap", async () => await Publish(), "SuccessButton", ""));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, Gap, 0, 0);
        stack.Children.Add(buttons);
        return stack;
    }

    private FrameworkElement ClientForm()
    {
        var stack = new StackPanel();
        var address = Text(store.Setting("clientAddress"));
        var port = Text(store.Setting("clientPort", Hub.DefaultPort.ToString(CultureInfo.InvariantCulture)));
        var user = Text(store.Setting("clientUser"));
        var password = new PasswordBox();
        var found = new StackPanel();
        var scanState = Label("Ağı tara ile bu ağda yayın yapan merkezi sunucuları bul; seçince IP ve port otomatik dolar.", 12, Muted);
        scanState.TextWrapping = TextWrapping.Wrap;
        Button? scan = null;
        scan = Act("Ağı tara", async () =>
        {
            scan!.IsEnabled = false;
            scanState.Text = "Taranıyor…";
            found.Children.Clear();
            try
            {
                var hubs = await Hub.ScanAsync(TimeSpan.FromSeconds(2));
                var own = store.Setting("mode") == "hub" ? hub.Id : "";
                hubs.RemoveAll(h => h.Id == own);
                scanState.Text = hubs.Count == 0
                    ? "Merkezi sunucu bulunamadı. Sunucu bilgisayarda Aden'in açık olduğundan, aynı ağda olduğunuzdan ve güvenlik duvarı izninden emin ol; IP'yi elle de girebilirsin."
                    : $"{hubs.Count} merkezi sunucu bulundu. Bağlanmak istediğini seç.";
                foreach (var item in hubs)
                {
                    var row = new DockPanel();
                    var pick = Act("Seç", () =>
                    {
                        address.Text = item.Address;
                        port.Text = item.Port.ToString(CultureInfo.InvariantCulture);
                        user.Focus();
                        Toast($"{item.Name} seçildi. Sunucu kullanıcı adı ve şifresini gir.");
                    }, "PrimaryButton");
                    DockPanel.SetDock(pick, Dock.Right);
                    row.Children.Add(pick);
                    var icon = Glyph("", 18, "#2563EB");
                    icon.Margin = new Thickness(0, 0, 12, 0);
                    DockPanel.SetDock(icon, Dock.Left);
                    row.Children.Add(icon);
                    var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    texts.Children.Add(Label(item.Name, 14, bold: true));
                    texts.Children.Add(Label($"{item.Address}:{item.Port} · Aden {item.Version}", 12, Muted));
                    row.Children.Add(texts);
                    found.Children.Add(new Border
                    {
                        Child = row, Padding = new Thickness(14, 10, 10, 10), Margin = new Thickness(0, SmallGap, 0, 0),
                        Background = Paint("#EFF4FF"), CornerRadius = new CornerRadius(10), BorderBrush = Paint("#BFDBFE"), BorderThickness = new Thickness(1)
                    });
                }
            }
            catch (Exception error) { scanState.Text = "Tarama yapılamadı: " + error.Message; }
            finally { scan.IsEnabled = true; }
        }, "PrimaryButton", "");
        var scanRow = new DockPanel();
        DockPanel.SetDock(scan, Dock.Right);
        scanRow.Children.Add(scan);
        scanState.Margin = new Thickness(0, 0, Gap, 0);
        scanState.VerticalAlignment = VerticalAlignment.Center;
        scanRow.Children.Add(scanState);
        stack.Children.Add(scanRow);
        stack.Children.Add(found);
        var form = Form(2, F(Field("Merkezi sunucu IP adresi", address, true)), F(Field("Port", port, true)),
            F(Field("Sunucu kullanıcı adı", user, true)), F(Field("Sunucu şifresi", password, true)));
        form.Margin = new Thickness(0, Gap, 0, 0);
        stack.Children.Add(form);
        var hint = Label("Sunucu kullanıcı adı ve şifresi, merkezi sunucu ayarlanırken belirlenen cihaz bağlantı bilgileridir. " +
            "Bağlandıktan sonra uygulamaya merkezi sunucudaki kullanıcı hesaplarıyla giriş yapılır.", 12, Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, SmallGap, 0, 0);
        stack.Children.Add(hint);

        async Task<(string Id, string Name)> Test()
        {
            if (!IPAddress.TryParse(address.Text.Trim(), out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new InvalidOperationException("Geçerli bir IPv4 adresi gir (ör. 192.168.1.10).");
            if (!int.TryParse(port.Text.Trim(), out var portNumber) || portNumber is < 1 or > 65535)
                throw new InvalidOperationException("Geçerli bir port gir.");
            if (user.Text.Trim().Length == 0 || password.Password.Length == 0)
                throw new InvalidOperationException("Sunucu kullanıcı adı ve şifresini gir.");
            using var http = Connection.Client(ip.ToString(), portNumber, user.Text, password.Password, TimeSpan.FromSeconds(5));
            Connection.AddIdentity(http, store, session.Username);
            try
            {
                using var response = await http.GetAsync("sync/ping");
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException("Sunucu kullanıcı adı veya şifresi hatalı.");
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new InvalidOperationException("Çok fazla hatalı deneme. 1 dakika sonra tekrar dene.");
                response.EnsureSuccessStatusCode();
                var info = await response.Content.ReadFromJsonAsync<HubPing>()
                    ?? throw new InvalidOperationException("Sunucu geçersiz yanıt verdi.");
                return (info.Id, info.Name);
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException($"{ip}:{portNumber} adresine ulaşılamadı. Sunucunun açık ve aynı ağda olduğunu kontrol et.");
            }
        }
        var buttons = Hstack(
            Act("Bağlantıyı test et", async () => { var info = await Test(); Toast($"Bağlantı başarılı · {info.Name}"); }, null, ""),
            Act("Bağlan", async () =>
            {
                if (syncing) throw new InvalidOperationException("Eşitleme tamamlanınca tekrar dene.");
                var info = await Test();
                if (store.Setting("mode") == "hub" && info.Id == hub.Id) throw new InvalidOperationException("Bu cihaz zaten o merkezi sunucu.");
                await hub.StopAsync();
                store.SaveConnection("client", "hub:" + info.Id, new Dictionary<string, string>
                {
                    ["clientAddress"] = address.Text.Trim(), ["clientPort"] = port.Text.Trim(),
                    ["clientUser"] = Users.Normalize(user.Text), ["clientPassword"] = Secrets.Protect(password.Password),
                    ["hubName"] = info.Name
                });
                ConnectionChanged($"Merkezi sunucu ({info.Name})");
            }, "SuccessButton", ""));
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, Gap, 0, 0);
        stack.Children.Add(buttons);
        return stack;
    }

    private sealed record HubPing(string Id, string Name);

    private Border DevicesCard()
    {
        var list = new StackPanel();
        var summary = Label("", 13, "#374151", true);
        summary.Margin = new Thickness(0, 0, 0, SmallGap);
        string signature = "";
        void Render()
        {
            var devices = store.Devices();
            var now = DateTimeOffset.UtcNow;
            var key = string.Join("|", devices.Select(d => $"{d.Id}{d.Name}{d.Address}{d.User}{d.LastSeen:HHmmss}{d.Requests}{now - d.LastSeen < Hub.OnlineWindow}"));
            var online = devices.Count(d => now - d.LastSeen < Hub.OnlineWindow);
            summary.Text = devices.Count == 0 ? "Henüz bağlanan cihaz yok." : $"{devices.Count} cihaz kayıtlı · {online} bağlı";
            if (key == signature) return;
            signature = key;
            list.Children.Clear();
            foreach (var device in devices)
            {
                var connected = now - device.LastSeen < Hub.OnlineWindow;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var names = new StackPanel();
                names.Children.Add(Label(device.Name, 14, bold: true));
                names.Children.Add(Label($"Son görülme {device.LastSeen.ToLocalTime():dd.MM HH:mm:ss} · {device.Requests:N0} istek", 11, Muted));
                row.Children.Add(names);
                var ip = Label(device.Address, 13, "#374151");
                ip.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(ip, 1);
                row.Children.Add(ip);
                var user = Label(device.User.Length > 0 ? device.User : "—", 13, "#374151");
                user.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(user, 2);
                row.Children.Add(user);
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center, Background = Paint(connected ? "#DCFCE7" : "#F3F4F6"),
                    Child = Label(connected ? "● Bağlı" : "○ Bağlı değil", 12, connected ? "#15803D" : "#6B7280", true)
                };
                Grid.SetColumn(badge, 3);
                row.Children.Add(badge);
                var forget = Act("", () => ConfirmDelete(device.Name, () => { store.ForgetDevice(device.Id); signature = ""; Render(); }), "GhostButton", "");
                forget.Width = forget.Height = forget.MinHeight = 32;
                forget.Padding = new Thickness(0);
                forget.ToolTip = "Listeden kaldır";
                forget.IsEnabled = !connected;
                Grid.SetColumn(forget, 4);
                row.Children.Add(forget);
                list.Children.Add(new Border
                {
                    Child = row, Padding = new Thickness(0, 10, 0, 10),
                    BorderBrush = Paint("#F0F1F4"), BorderThickness = new Thickness(0, 1, 0, 0)
                });
            }
        }
        Render();
        refreshDevices = Render;
        var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        string[] titles = ["Cihaz", "İç IP", "Kullanıcı", "Durum"];
        for (var i = 0; i < 5; i++)
            header.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i switch { 0 => new GridLength(1.4, GridUnitType.Star), 3 => new GridLength(120), 4 => new GridLength(32), _ => new GridLength(1, GridUnitType.Star) }
            });
        for (var i = 0; i < titles.Length; i++)
        {
            var title = Label(titles[i], 12, Muted, true);
            Grid.SetColumn(title, i);
            header.Children.Add(title);
        }
        var content = new StackPanel();
        content.Children.Add(summary);
        content.Children.Add(header);
        content.Children.Add(list);
        return SettingsCard("Bağlı cihazlar", "Bu merkezi sunucuya bağlanan kasalar. Son 15 saniyede eşitleme yapan cihaz bağlı sayılır; liste 3 saniyede bir yenilenir.", content);
    }

    // ───────────── Kullanıcılar ─────────────

    private void AddUserSettings(Panel panel)
    {
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            var users = Users.Resolve(store.ReadKind("user")).Values.OrderBy(u => u.Username != Users.Admin).ThenBy(u => u.Name, StringComparer.Create(culture, true));
            foreach (var user in users)
            {
                var row = new DockPanel();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(actions, Dock.Right);
                row.Children.Add(actions);
                var edit = Act("Düzenle", () => EditUser(user), null, "");
                Append(actions, edit);
                if (user.Username != Users.Admin && user.Username != session.Username)
                {
                    var remove = Act("", () => ConfirmDelete(user.Name, () =>
                    {
                        Save("user", user with { Archived = true });
                        Toast($"{user.Name} silindi.");
                    }), "DangerButton", "");
                    remove.ToolTip = "Kullanıcıyı sil";
                    System.Windows.Automation.AutomationProperties.SetName(remove, $"{user.Name} sil");
                    Append(actions, remove);
                }
                var avatar = new Border
                {
                    Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 12, 0),
                    Background = Paint(user.Role == Users.AdminRole ? "#DBEAFE" : "#F3F4F6"),
                    Child = new TextBlock
                    {
                        Text = user.Name.Length > 0 ? char.ToUpper(user.Name[0], culture).ToString() : "?", FontWeight = FontWeights.Bold,
                        Foreground = Paint(user.Role == Users.AdminRole ? "#1D4ED8" : "#374151"),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                DockPanel.SetDock(avatar, Dock.Left);
                row.Children.Add(avatar);
                var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                texts.Children.Add(Label(user.Name + (user.Username == session.Username ? " (sen)" : ""), 14, bold: true));
                texts.Children.Add(Label($"{user.Username} · {user.Role}" + (user.MustChange ? " · ilk girişte şifre değiştirecek" : ""), 12, Muted));
                row.Children.Add(texts);
                list.Children.Add(new Border
                {
                    Child = row, Padding = new Thickness(0, 10, 0, 10),
                    BorderBrush = Paint("#F0F1F4"), BorderThickness = new Thickness(0, list.Children.Count == 0 ? 0 : 1, 0, 0)
                });
            }
        }
        Render();
        refreshers.Add(Render);
        panel.Children.Add(SettingsCard("Kullanıcılar",
            "Her bilgisayar kendi kullanıcısıyla giriş yapar. Yöneticiler kullanıcı ekleyebilir ve Ayarlar'a erişebilir. " +
            "Merkezi sunucuya bağlı cihazlarda kullanıcılar sunucudan doğrulanır.",
            list, Act("Kullanıcı ekle", () => EditUser(null), "PrimaryButton", "")));
    }

    private void EditUser(UserAccount? user)
    {
        var name = Text(user?.Name ?? "");
        name.MaxLength = 120;
        var username = Text(user?.Username ?? "");
        username.MaxLength = 32;
        username.IsReadOnly = user is not null;
        var role = new ComboBox { ItemsSource = new[] { Users.UserRole, Users.AdminRole }, SelectedItem = user?.Role ?? Users.UserRole };
        role.IsEnabled = user?.Username != Users.Admin && user?.Username != session.Username;
        var password = new PasswordBox { MaxLength = 128 };
        var repeat = new PasswordBox { MaxLength = 128 };
        var mustChange = new CheckBox { Content = "İlk girişte şifresini değiştirsin", IsChecked = user is null };
        var form = Form(2,
            F(Field("Ad soyad", name, true), 2),
            F(Field("Kullanıcı adı", username, true)), F(Field("Yetki", role, true)),
            F(Field(user is null ? "Şifre" : "Yeni şifre (değiştirmek için doldur)", password, user is null)),
            F(Field("Şifre (tekrar)", repeat, user is null)),
            F(mustChange, 2));
        var modal = ShowModal(user is null ? "Kullanıcı ekle" : "Kullanıcıyı düzenle", form, 560,
            subtitle: "Kullanıcı adı küçük harf, rakam ve . _ - içerebilir. Şifre en az 6 karakter.");
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        Append(modal.Footer, Act("Kaydet", () =>
        {
            var login = Users.Normalize(username.Text);
            if (name.Text.Trim().Length == 0) throw new InvalidOperationException("Ad soyad boş olamaz.");
            if (!Users.ValidUsername(login) || login == Users.Developer)
                throw new InvalidOperationException("Kullanıcı adı 3-32 karakter olmalı (harf, rakam, . _ -).");
            var users = Users.Resolve(store.ReadKind("user"));
            if (user is null && users.ContainsKey(login)) throw new InvalidOperationException("Bu kullanıcı adı zaten kullanılıyor.");
            var selectedRole = (string)role.SelectedItem;
            if (password.Password.Length > 0 || user is null)
            {
                if (password.Password.Length < 6) throw new InvalidOperationException("Şifre en az 6 karakter olmalı.");
                if (password.Password != repeat.Password) throw new InvalidOperationException("Şifreler aynı değil.");
            }
            var saved = password.Password.Length > 0
                ? Users.Create(login, name.Text, selectedRole, password.Password, mustChange.IsChecked == true)
                : user! with { Name = name.Text.Trim(), Role = selectedRole, MustChange = mustChange.IsChecked == true };
            Save("user", saved);
            modal.Close();
            Toast(user is null ? $"{saved.Name} eklendi." : $"{saved.Name} güncellendi.");
        }, "SuccessButton", ""));
    }

    // ───────────── Developers ─────────────

    private void OpenDevelopers()
    {
        if (developerUnlocked) { SelectPage(developersPage!); return; }
        var username = Text();
        var password = new PasswordBox();
        var form = Form(1, F(Field("Kullanıcı adı", username)), F(Field("Şifre", password)));
        var modal = ShowModal("Developers", form, 420, subtitle: "Bu bölüm yalnızca geliştirici hesabıyla açılır.");
        Append(modal.Footer, Act("Vazgeç", modal.Close));
        Append(modal.Footer, Act("Aç", () =>
        {
            if (!Users.IsDeveloper(username.Text, password.Password))
            {
                store.Log("Hata", session.Username, Environment.MachineName, "yerel", "Developers girişi reddedildi", Users.Normalize(username.Text), 401);
                password.Clear();
                throw new InvalidOperationException("Geliştirici bilgileri hatalı.");
            }
            store.Log("Giriş", session.Username, Environment.MachineName, "yerel", "Developers açıldı");
            developerUnlocked = true;
            UpdateNavigationAccess();
            modal.Close();
            SelectPage(developersPage!);
        }, "PrimaryButton", ""));
    }

    private void AddDevelopers()
    {
        developerUnlocked = session.Developer;
        var body = AddPage("Developers", "Kullanım logları ve sistem bilgileri. Yalnızca geliştirici erişimi.", "", out var actions);
        developersPage = pages[^1].Content;
        var logs = LogsView(actions);
        var system = new StackPanel();
        void ShowSystem()
        {
            system.Children.Clear();
            void Row(string label, string value)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var title = Label(label, 13, Muted);
                title.Width = 220;
                DockPanel.SetDock(title, Dock.Left);
                row.Children.Add(title);
                var text = new TextBox { Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0 };
                row.Children.Add(text);
                system.Children.Add(row);
            }
            Row("Sürüm", Hub.Version);
            Row("Çalışma modu", Connection.Describe(store));
            Row("Cihaz adı", Environment.MachineName);
            Row("Cihaz kimliği", store.Setting("deviceId"));
            Row("Veritabanı", store.Path);
            Row("Kayıt sayısı", snapshot.Length.ToString("N0", culture));
            Row("Bekleyen (gönderilmemiş) kayıt", store.Read(true).Count.ToString(culture));
            Row("Yerel IPv4 adresleri", string.Join(", ", Hub.LocalAddresses()));
            if (store.Setting("mode") == "hub")
            {
                Row("Merkezi sunucu", hub.Running ? $"Yayında · {hub.Address}:{hub.Port}" : "Kapalı · " + hub.Error);
                Row("Sunucu kimliği", hub.Id);
                Row("Sunucu kullanıcı adı", store.Setting("hubUser"));
            }
            Row("Oturum", $"{session.Name} ({session.Username}) · {session.Role}" + (session.Offline ? " · çevrimdışı" : ""));
        }
        ShowSystem();
        refreshers.Add(ShowSystem);
        var systemCard = Card(TouchScroll(system), new Thickness(PagePadding));
        body.Children.Add(Segments(out _, ("Kullanım logları", logs), ("Sistem", systemCard)));
    }

    private sealed record LogRow(DateTime Zaman, string Tür, string Kullanıcı, string Cihaz, string IP, string İşlem, string Detay, int Durum, int Süre);

    private UIElement LogsView(StackPanel actions)
    {
        var source = new ComboBox { Width = 170, ItemsSource = new[] { "Bu cihaz", "Merkezi sunucu" }, SelectedIndex = 0 };
        source.Visibility = store.Setting("mode") == "client" ? Visibility.Visible : Visibility.Collapsed;
        var from = new DatePicker { SelectedDate = DateTime.Today.AddDays(-7), Width = 140 };
        var to = new DatePicker { SelectedDate = DateTime.Today, Width = 140 };
        var kind = new ComboBox { Width = 130, ItemsSource = new[] { "Tüm türler", "Giriş", "Çıkış", "İşlem", "İstek", "Hata", "Sistem" }, SelectedIndex = 0 };
        var user = new ComboBox { Width = 140, IsEditable = true };
        var device = new ComboBox { Width = 150, IsEditable = true };
        var search = SearchField("İşlem, detay veya IP ara", out var searchBox);
        search.Width = 240;
        var count = Label("", 12, Muted);
        count.VerticalAlignment = VerticalAlignment.Center;
        var grid = Table();
        grid.AutoGeneratingColumn += (_, e) =>
        {
            if (e.PropertyName == "Zaman" && e.Column is DataGridTextColumn { Binding: System.Windows.Data.Binding binding })
                binding.StringFormat = "dd.MM.yyyy HH:mm:ss";
            if (e.PropertyName == "Süre") e.Column.Header = "Süre (ms)";
            e.Column.Width = e.PropertyName switch
            {
                "Detay" => new DataGridLength(2.4, DataGridLengthUnitType.Star),
                "İşlem" => new DataGridLength(1.6, DataGridLengthUnitType.Star),
                "Durum" or "Süre" or "Tür" => new DataGridLength(80),
                "Zaman" => new DataGridLength(150),
                _ => new DataGridLength(1, DataGridLengthUnitType.Star)
            };
        };
        var loading = false;
        async Task Load()
        {
            if (loading) return;
            loading = true;
            try
            {
                DateTimeOffset? start = from.SelectedDate is DateTime f ? new DateTimeOffset(f.Date) : null;
                DateTimeOffset? end = to.SelectedDate is DateTime t ? new DateTimeOffset(t.Date.AddDays(1)) : null;
                var kindText = kind.SelectedIndex > 0 ? (string)kind.SelectedItem : "";
                List<UsageLog> rows;
                if (source.SelectedIndex == 1)
                {
                    using var http = Connection.Create(store, session.Username, TimeSpan.FromSeconds(8))
                        ?? throw new InvalidOperationException("Merkezi sunucuya bağlı değilsin.");
                    string Q(string value) => Uri.EscapeDataString(value);
                    var query = $"sync/logs?kind={Q(kindText)}&user={Q(user.Text.Trim())}&device={Q(device.Text.Trim())}&search={Q(searchBox.Text.Trim())}" +
                        (start is { } s ? "&from=" + Q(s.UtcDateTime.ToString("O")) : "") + (end is { } e ? "&to=" + Q(e.UtcDateTime.ToString("O")) : "");
                    try { rows = await http.GetFromJsonAsync<List<UsageLog>>(query) ?? new(); }
                    catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
                    {
                        throw new InvalidOperationException("Merkezi sunucunun logları alınamadı: " + error.Message);
                    }
                }
                else rows = store.Logs(start, end, kindText, user.Text.Trim(), device.Text.Trim(), searchBox.Text.Trim());
                grid.ItemsSource = rows.Select(l => new LogRow(l.At.LocalDateTime, l.Kind, l.User, l.Device, l.Address, l.Action, l.Detail, l.Status, l.Duration)).ToArray();
                count.Text = rows.Count >= 2000 ? "Son 2.000 kayıt gösteriliyor" : $"{rows.Count:N0} kayıt";
                Fill(user, rows.Select(r => r.User));
                Fill(device, rows.Select(r => r.Device));
            }
            catch (Exception error) { Toast(error.Message, true); }
            finally { loading = false; }
        }
        void Fill(ComboBox box, IEnumerable<string> values)
        {
            var text = box.Text;
            var items = values.Where(v => v.Length > 0).Distinct().OrderBy(v => v, StringComparer.Create(culture, true)).ToList();
            foreach (var existing in box.Items.Cast<string>()) if (!items.Contains(existing)) items.Add(existing);
            box.ItemsSource = items;
            box.Text = text;
        }
        source.SelectionChanged += async (_, _) => await Load();
        from.SelectedDateChanged += async (_, _) => await Load();
        to.SelectedDateChanged += async (_, _) => await Load();
        kind.SelectionChanged += async (_, _) => await Load();
        user.SelectionChanged += async (_, _) => { if (user.SelectedItem is string s) { user.Text = s; await Load(); } };
        device.SelectionChanged += async (_, _) => { if (device.SelectedItem is string s) { device.Text = s; await Load(); } };
        searchBox.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Load(); };
        Append(actions, Act("Filtreleri temizle", async () =>
        {
            from.SelectedDate = DateTime.Today.AddDays(-7);
            to.SelectedDate = DateTime.Today;
            kind.SelectedIndex = 0;
            user.Text = device.Text = searchBox.Text = "";
            await Load();
        }, null, ""));
        Append(actions, Act("Yenile", async () => await Load(), "PrimaryButton", ""));

        var filters = new WrapPanel { Orientation = Orientation.Horizontal };
        void Add(string label, FrameworkElement input)
        {
            var field = Field(label, input);
            field.Margin = new Thickness(0, 0, 12, 8);
            if (input.Visibility == Visibility.Collapsed) field.Visibility = Visibility.Collapsed;
            filters.Children.Add(field);
        }
        Add("Kaynak", source);
        Add("Başlangıç", from);
        Add("Bitiş", to);
        Add("Tür", kind);
        Add("Kullanıcı", user);
        Add("Cihaz", device);
        Add("Ara", search);
        var top = new DockPanel();
        DockPanel.SetDock(count, Dock.Right);
        top.Children.Add(count);
        top.Children.Add(filters);
        grid.Loaded += async (_, _) => await Load();
        return TableCard(grid, top);
    }
}
