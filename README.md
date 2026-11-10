# Aden

Windows ön muhasebe, depo ve hızlı satış uygulaması. Bu ilk sürüm tek satış kasası için yerel SQLite ve isteğe bağlı sunucu eşitlemesiyle çalışır.

## Windows

Repo kökünde .NET 10 SDK ile:

```powershell
dotnet run --project muhasebe/Aden.Desktop.csproj
```

Varsayılan **Yerel mod** ağ isteği göndermez. Veriler `%LOCALAPPDATA%\Aden\aden.db` dosyasındadır. Bir test profili açmak için `-- --db C:\test\aden.db` kullan. Uygulama kapalıyken dosyanın yedeğini alabilirsin.

## Yerel test sunucusu

Docker Desktop Linux engine çalışırken, repo kökünden PowerShell'de:

```powershell
$env:ADEN_DB_PASSWORD = [guid]::NewGuid().ToString('N')
$env:ADEN_SYNC_KEY = [guid]::NewGuid().ToString('N')
docker compose -f server/compose.yaml up -d --build
Invoke-WebRequest http://127.0.0.1:5080/health
```

Masaüstünde **Ayarlar** bölümünde sunucu eşitlemesini aç, `http://127.0.0.1:5080` ve `ADEN_SYNC_KEY` değerini kaydet. Yerelde birikmiş kayıtlar da bu sunucuya gönderilir. Mod kapatıldığında yeni istek çıkmaz. Tekrar açıldığında sıra kaldığı yerden devam eder. Docker'ı durdurup yerelde işlem girerek bağlantı kesintisini deneyebilirsin.

Yukarıdaki parolayı ilk kurulumda güvenli yerde sakla. PostgreSQL volume'u oluşturulduktan sonra yeni rastgele parola üretmek mevcut veritabanı parolasını değiştirmez. Test sunucusu tek işletme için ortak bir eşitleme anahtarı kullanır. Ayrı işletmeler için ayrı kurulum gerekir.

## Veri modeli

Ortak kayıt sözleşmeleri `shared` içinde. Yerel ve sunucu şeması sırasıyla `muhasebe/Migrations` ve `server/Migrations` altında. Her iş hareketi değişmez bir kayıt olarak saklanır. Bir satış kaydı sepeti ve ödeme parçalarını birlikte içerir. Yerelde stok ve bakiye bu hareketlerden hesaplanır. Sunucu işlem kimliğiyle tekrarları engeller ve değişiklikleri sıra numarasıyla verir.

İlk kapsam, tek aktif satış kasası içindir. Aynı anda birden çok kasa ile satışta stok tahsisi ve kart düzenleme çakışmalarının çözümü ayrıca tasarlanmalıdır. Yerelde kaydedilen kart ödemeleri manuel teyitlidir. Teklif ve mali olmayan satış özeti Windows yazdırma penceresinden yazdırılır. PDF için Windows PDF yazıcısı seçilebilir. Mali fiş, iade, e-Fatura ve ÖKC/POS bağlantısı seçilecek SDK ile ayrı doğrulama gerektirir.



## Kurulu Windows sürümü ve güncellemeler

Windows 64 bit kurulum dosyasını yerelde üretmek için repo kökünde:

```powershell
./scripts/Pack-Aden.ps1 -Version 1.0.0 -OutputDirectory ./output/windows
```

Kurulu uygulama `hbaydemir/aden` GitHub Releases üzerinden `win-x64` kanalını kontrol eder. Yeni sürümü arka planda indirir ve kullanıcı **Güncelle ve yeniden başlat** düğmesine basınca uygular. Açık satış veya teklif varken güncelleme uygulanmaz. Otomatik kontrol Ayarlar üzerinden kapatılabilir. Portable demo bu kanala bağlı değildir.

Yayına hazır commit için artan bir `vMAJOR.MINOR.PATCH` etiketi GitHub'a gönderildiğinde `windows-release.yml` çalışır. Workflow Setup, tam güncelleme paketi ve kanal manifestini önce taslak release'e yükler, sonra release'i yayımlar. Bir sürümün etiketi ve paketleri yayımlandıktan sonra değişiklikler yeni bir sürüm numarasıyla gönderilir.

Kurulum kimliği `Aden.Windows`. İş verisi ayrı olarak `%LOCALAPPDATA%\Aden\aden.db` yolunda kalır. Temiz kurulum örnek kayıt eklemez. Mevcut dosya yerinde kullanılır. Şema yükseltmesi gerektiğinde önce aynı veri dizinindeki `Yedekler` klasörüne SQLite yedeği alınır, ardından migration tek transaction içinde uygulanır.
