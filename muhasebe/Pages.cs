using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Aden;

public sealed partial class MainWindow
{
    // ───────────── Ayarlar ─────────────

    private Border SettingsCard(string title, string description, UIElement content, params Button[] buttons)
    {
        var stack = new StackPanel();
        stack.Children.Add(Label(title, 15, bold: true));
        var desc = Label(description, 13, Muted);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 4, 0, 20);
        stack.Children.Add(desc);
        stack.Children.Add(content);
        if (buttons.Length > 0)
        {
            var row = Hstack(buttons);
            row.HorizontalAlignment = HorizontalAlignment.Right;
            row.Margin = new Thickness(0, 20, 0, 0);
            stack.Children.Add(row);
        }
        var card = Card(stack, new Thickness(PagePadding));
        card.Margin = new Thickness(0, 0, 0, Gap);
        return card;
    }

    private void AddSettings()
    {
        var body = AddPage("Ayarlar", "Firma bilgileri, sunucu bağlantısı, kullanıcılar ve güncellemeler", "", out _);
        var panel = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        var scroll = TouchScroll(panel);
        scroll.Margin = new Thickness(0, 0, -PagePadding, 0);
        scroll.Padding = new Thickness(0, 0, PagePadding, 0);
        body.Children.Add(scroll);
        panel.Width = 760;

        var company = Text(store.Setting("company", "Aden"));
        company.MaxLength = 200;
        var phone = Text(store.Setting("companyPhone"));
        phone.MaxLength = 40;
        // Eski sürümlerde adres ve iletişim tek alandaydı.
        var address = Multiline(store.Setting("companyAddress", store.Setting("companyPhone").Length == 0 ? store.Setting("companyContact") : ""), 64);
        address.MaxLength = 1000;
        var email = Text(store.Setting("companyEmail"));
        email.MaxLength = 200;
        var web = Text(store.Setting("companyWeb"));
        web.MaxLength = 200;
        panel.Children.Add(SettingsCard("Firma bilgileri", "Teklif PDF'i, teklif çıktısı ve satış özetlerinde görünür.",
            Form(2, F(Field("Firma ünvanı", company, true), 2), F(Field("Telefon numarası", phone, true)), F(Field("E-posta", email)),
                F(Field("Adres", address, true), 2), F(Field("Web sitesi", web), 2)),
            Act("Kaydet", () =>
            {
                var title = company.Text.Trim();
                if (title.Length == 0) throw new InvalidOperationException("Firma ünvanı boş olamaz.");
                if (phone.Text.Trim().Length == 0) throw new InvalidOperationException("Telefon numarası boş olamaz.");
                if (address.Text.Trim().Length == 0) throw new InvalidOperationException("Adres boş olamaz.");
                if (email.Text.Trim().Length > 0 && !IsEmail(email.Text.Trim()))
                    throw new InvalidOperationException("Geçerli bir e-posta adresi yaz (ornek@firma.com).");
                store.SaveSetting("company", title);
                store.SaveSetting("companyPhone", phone.Text.Trim());
                store.SaveSetting("companyAddress", address.Text.Trim());
                store.SaveSetting("companyEmail", email.Text.Trim());
                store.SaveSetting("companyWeb", web.Text.Trim());
                store.SaveSetting("companyContact", string.Join(Environment.NewLine, new[]
                {
                    address.Text.Trim().ReplaceLineEndings(" "), "Tel: " + phone.Text.Trim(),
                    email.Text.Trim(), web.Text.Trim()
                }.Where(line => line.Length > 0)));
                Toast("Firma bilgileri kaydedildi.");
            }, "SuccessButton", "")));

        var defaultVat = VatBox(DefaultVat);
        defaultVat.Width = 160;
        defaultVat.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(SettingsCard("Vergi",
            "Yeni ürünlerde KDV oranı bu değerle gelir; ürün kartında ürüne özel oran seçebilirsin. KDV oranı kayıtlı olmayan eski ürünler de bu oranı kullanır.",
            Field("Varsayılan KDV oranı (%)", defaultVat),
            Act("Kaydet", () =>
            {
                store.SaveSetting("defaultVat", ReadVat(defaultVat).ToString(System.Globalization.CultureInfo.InvariantCulture));
                Refresh();
                Toast("Varsayılan KDV oranı kaydedildi.");
            }, "SuccessButton", "\uE74E")));

        var preview = new Image { Height = 56, MaxWidth = 220, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        void ShowLogo()
        {
            var configured = store.Setting("quoteLogo");
            if (configured.Length == 0) { preview.Source = Brand.Wordmark; return; }
            using var stream = new System.IO.MemoryStream(Convert.FromBase64String(configured));
            preview.Source = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        }
        ShowLogo();
        var logoBox = new Border
        {
            Child = preview, Background = Paint("#F9FAFB"), BorderBrush = Paint(LineColor), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(Gap), HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Children.Add(SettingsCard("Teklif logosu", "PNG veya JPG, en fazla 2 MB. Teklif PDF'i ve çıktısının üst köşesinde yer alır.", logoBox,
            Act("Varsayılana dön", () => { store.SaveSetting("quoteLogo", ""); ShowLogo(); Toast("Varsayılan logo kullanılacak."); }, "DangerButton"),
            Act("Logo seç", () =>
            {
                var picker = new OpenFileDialog { Filter = "Resim|*.png;*.jpg;*.jpeg" };
                if (picker.ShowDialog(this) != true) return;
                var file = new System.IO.FileInfo(picker.FileName);
                if (file.Length > 2_000_000) throw new InvalidOperationException("Logo en fazla 2 MB olabilir.");
                var bytes = System.IO.File.ReadAllBytes(picker.FileName);
                using var image = new System.IO.MemoryStream(bytes);
                BitmapDecoder.Create(image, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                store.SaveSetting("quoteLogo", Convert.ToBase64String(bytes));
                ShowLogo();
                Toast("Teklif logosu güncellendi.");
            }, "PrimaryButton", "")));

        AddConnectionSettings(panel);
        AddUserSettings(panel);

        AddUpdateSettings(panel);

        var note = Label("E-Fatura ve ÖKC sağlayıcısı henüz yapılandırılmadı. Kart kayıtları manuel teyit gerektirir.", 13, "#374151");
        note.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(SettingsCard("Entegrasyonlar", "Mali belge ve ödeme cihazı bağlantıları.", note));
    }
}
