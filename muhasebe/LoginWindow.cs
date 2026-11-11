using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Aden;

/// <summary>Oturum: giriş yapan kullanıcı. Developer yalnızca dev hesabıyla açılır.</summary>
public sealed record Session(string Username, string Name, string Role, bool Developer = false, bool Offline = false)
{
    public bool IsAdmin => Role == Users.AdminRole || Developer;
}

/// <summary>Kullanıcı girişi. Merkezi veya uzak sunucuya bağlı cihazlarda kullanıcılar sunucudan sorgulanır;
/// sunucuya ulaşılamazsa son eşitlenen kullanıcı listesiyle çevrimdışı giriş yapılır.</summary>
public sealed class LoginWindow : Window
{
    private readonly Store store;
    private readonly TaskCompletionSource<Session?> result = new();
    private readonly TextBox username = new() { MaxLength = 32 };
    private readonly PasswordBox password = new() { MaxLength = 128 };
    private readonly TextBlock message = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Button submit;
    private readonly StackPanel loginPanel = new();
    private readonly StackPanel changePanel = new() { Visibility = Visibility.Collapsed };
    private int failures;
    private DateTime lockedUntil;
    private bool busy;

    public Task<Session?> Result => result.Task;

    public LoginWindow(Store store)
    {
        this.store = store;
        Title = "Aden | Giriş";
        Icon = Brand.Mark;
        Width = 980;
        Height = 640;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage("tr-TR");
        Background = Paint("#F4F6FA");

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        Content = layout;

        // Sol: marka paneli.
        var hero = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(0x1E, 0x3A, 0x8A), Color.FromRgb(0x25, 0x63, 0xEB), 45),
            Padding = new Thickness(48)
        };
        var heroStack = new DockPanel();
        hero.Child = heroStack;
        var mark = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(18), Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10),
            Child = new Image { Source = Brand.Mark, Stretch = Stretch.Uniform }
        };
        DockPanel.SetDock(mark, Dock.Top);
        heroStack.Children.Add(mark);
        var footer = new TextBlock
        {
            Text = Connection.Describe(store), Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontSize = 12, TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        heroStack.Children.Add(footer);
        var pitch = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        pitch.Children.Add(new TextBlock { Text = "Aden", Foreground = Brushes.White, FontSize = 40, FontWeight = FontWeights.Bold });
        pitch.Children.Add(new TextBlock
        {
            Text = "Ön muhasebe, stok ve satış. Aynı ağdaki tüm kasalar tek veritabanında.",
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)), FontSize = 16,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Left
        });
        heroStack.Children.Add(pitch);
        layout.Children.Add(hero);

        // Sağ: giriş kartı.
        var card = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(16), Padding = new Thickness(36, 32, 36, 32),
            Width = 400, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.10 }
        };
        Grid.SetColumn(card, 1);
        layout.Children.Add(card);
        var body = new StackPanel();
        card.Child = body;
        body.Children.Add(loginPanel);
        body.Children.Add(changePanel);

        loginPanel.Children.Add(new TextBlock { Text = "Giriş yap", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Paint("#111827") });
        loginPanel.Children.Add(new TextBlock
        {
            Text = Connection.IsNetworked(store) ? "Kullanıcı bilgileri merkezi sunucudan doğrulanır." : "Bu cihazdaki kullanıcı hesabınla giriş yap.",
            FontSize = 13, Foreground = Paint("#6B7280"), Margin = new Thickness(0, 6, 0, 24), TextWrapping = TextWrapping.Wrap
        });
        System.Windows.Automation.AutomationProperties.SetName(username, "Kullanıcı adı");
        System.Windows.Automation.AutomationProperties.SetName(password, "Şifre");
        loginPanel.Children.Add(Field("Kullanıcı adı", username));
        loginPanel.Children.Add(Field("Şifre", password, 14));
        message.Margin = new Thickness(0, 14, 0, 0);
        loginPanel.Children.Add(message);
        submit = new Button
        {
            Content = "Giriş yap", Style = (Style)Application.Current.FindResource("PrimaryButton"),
            Margin = new Thickness(0, 22, 0, 0), MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        submit.Click += async (_, _) => await LoginAsync();
        loginPanel.Children.Add(submit);
        if (!Connection.IsNetworked(store) && Users.Resolve(store.ReadKind("user"))[Users.Admin] == Users.DefaultAdmin)
            loginPanel.Children.Add(new TextBlock
            {
                Text = "İlk kurulum: kullanıcı adı admin, şifre admin. İlk girişte yeni şifre belirlenir.",
                FontSize = 12, Foreground = Paint("#6B7280"), Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap
            });

        username.Text = store.Setting("lastUser");
        KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (changePanel.Visibility == Visibility.Visible) changeAction?.Invoke();
            else await LoginAsync();
        };
        Loaded += (_, _) => { if (username.Text.Length > 0) password.Focus(); else username.Focus(); };
        Closed += (_, _) => result.TrySetResult(null);
    }

    private Action? changeAction;

    private static SolidColorBrush Paint(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private static FrameworkElement Field(string label, Control input, double top = 0)
    {
        var stack = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Paint("#374151"), Margin = new Thickness(0, 0, 0, 6) });
        input.MinHeight = 42;
        stack.Children.Add(input);
        return stack;
    }

    private void Show(string text, bool error = true)
    {
        message.Text = text;
        message.Foreground = Paint(error ? "#DC2626" : "#2563EB");
        message.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoginAsync()
    {
        if (busy) return;
        var name = Users.Normalize(username.Text);
        var secret = password.Password;
        if (name.Length == 0 || secret.Length == 0) { Show("Kullanıcı adı ve şifreyi gir."); return; }
        if (lockedUntil > DateTime.Now)
        {
            Show($"Çok fazla hatalı deneme. {(int)Math.Ceiling((lockedUntil - DateTime.Now).TotalSeconds)} saniye sonra tekrar dene.");
            return;
        }
        busy = true;
        submit.IsEnabled = false;
        submit.Content = "Kontrol ediliyor…";
        Show("");
        try
        {
            if (Users.IsDeveloper(name, secret))
            {
                Finish(new Session(Users.Developer, "Developer", Users.AdminRole, Developer: true));
                return;
            }
            var (login, offline, failure) = await VerifyAsync(name, secret);
            if (login is null)
            {
                store.Log("Hata", name, Environment.MachineName, "yerel", "Başarısız giriş denemesi", failure, 401);
                if (++failures >= 5) { lockedUntil = DateTime.Now.AddSeconds(30); failures = 0; }
                Show(failure);
                password.Clear();
                password.Focus();
                return;
            }
            failures = 0;
            var session = new Session(login.Username, login.Name, login.Role, Offline: offline);
            if (login.MustChange) { AskNewPassword(session, secret); return; }
            Finish(session);
        }
        finally
        {
            busy = false;
            submit.IsEnabled = true;
            submit.Content = "Giriş yap";
        }
    }

    private async Task<(LoginResult? Login, bool Offline, string Failure)> VerifyAsync(string name, string secret)
    {
        const string wrong = "Kullanıcı adı veya şifre hatalı.";
        if (!Connection.IsNetworked(store))
            return (Users.Login(store.ReadKind("user"), name, secret), false, wrong);
        try
        {
            using var http = Connection.Create(store, name, TimeSpan.FromSeconds(5))!;
            using var response = await http.PostAsJsonAsync("sync/login", new LoginRequest(name, secret));
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (null, false, await IsDeviceRejected(http)
                    ? "Merkezi sunucu bu cihazın sunucu kullanıcı adı/şifresini kabul etmedi. Ayarlar > Bağlantı bölümünü kontrol et."
                    : wrong);
            }
            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, false, "Sunucu kullanıcı girişini desteklemiyor. Sunucuyu güncelle.");
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<LoginResult>(), false, wrong);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Sunucuya ulaşılamadı: son eşitlenen kullanıcılarla devam et.
            var local = Users.Login(store.ReadKind("user"), name, secret);
            return (local, true, local is null
                ? "Sunucuya ulaşılamadı ve bu bilgilerle çevrimdışı giriş yapılamadı."
                : wrong);
        }
    }

    /// <summary>401 kullanıcı şifresinden mi yoksa cihaz kimlik bilgilerinden mi geldi?</summary>
    private static async Task<bool> IsDeviceRejected(HttpClient http)
    {
        try
        {
            using var ping = await http.GetAsync("sync/ping");
            return ping.StatusCode == HttpStatusCode.Unauthorized;
        }
        catch (HttpRequestException) { return false; }
    }

    private void AskNewPassword(Session session, string current)
    {
        loginPanel.Visibility = Visibility.Collapsed;
        changePanel.Visibility = Visibility.Visible;
        changePanel.Children.Clear();
        changePanel.Children.Add(new TextBlock { Text = "Yeni şifre belirle", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = Paint("#111827") });
        changePanel.Children.Add(new TextBlock
        {
            Text = $"{session.Name}, güvenliğin için varsayılan şifreyi değiştirmen gerekiyor. En az 6 karakter.",
            FontSize = 13, Foreground = Paint("#6B7280"), Margin = new Thickness(0, 6, 0, 24), TextWrapping = TextWrapping.Wrap
        });
        var first = new PasswordBox { MaxLength = 128 };
        var second = new PasswordBox { MaxLength = 128 };
        System.Windows.Automation.AutomationProperties.SetName(first, "Yeni şifre");
        System.Windows.Automation.AutomationProperties.SetName(second, "Yeni şifre tekrar");
        changePanel.Children.Add(Field("Yeni şifre", first));
        changePanel.Children.Add(Field("Yeni şifre (tekrar)", second, 14));
        var error = new TextBlock { FontSize = 13, Foreground = Paint("#DC2626"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        changePanel.Children.Add(error);
        var save = new Button
        {
            Content = "Şifreyi kaydet ve devam et", Style = (Style)Application.Current.FindResource("PrimaryButton"),
            Margin = new Thickness(0, 22, 0, 0), MinHeight = 44
        };
        changeAction = () =>
        {
            if (first.Password.Length < 6) { error.Text = "Şifre en az 6 karakter olmalı."; return; }
            if (first.Password != second.Password) { error.Text = "Şifreler aynı değil."; return; }
            if (first.Password == current) { error.Text = "Yeni şifre eskisiyle aynı olamaz."; return; }
            var existing = Users.Resolve(store.ReadKind("user")).GetValueOrDefault(session.Username);
            store.Add("user", Users.Create(session.Username, existing?.Name ?? session.Name, existing?.Role ?? session.Role, first.Password));
            store.Log("İşlem", session.Username, Environment.MachineName, "yerel", "Şifre değiştirildi", session.Username);
            Finish(session);
        };
        save.Click += (_, _) => changeAction();
        changePanel.Children.Add(save);
        first.Focus();
    }

    private void Finish(Session session)
    {
        store.SaveSetting("lastUser", session.Developer ? store.Setting("lastUser") : session.Username);
        store.Log("Giriş", session.Username, Environment.MachineName, "yerel",
            session.Offline ? "Çevrimdışı giriş (sunucuya ulaşılamadı)" : "Giriş yapıldı", $"{session.Name} · {session.Role}");
        result.TrySetResult(session);
    }
}
