using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Aden;

/// <summary>Bu cihazı yerel ağda merkezi sunucu yapan gömülü HTTP sunucusu ve UDP keşif yanıtlayıcısı.
/// İstemciler uzak sunucuyla aynı /sync protokolünü kullanır; kimlik doğrulama cihaz (sunucu) kullanıcı adı ve şifresiyle yapılır.</summary>
public sealed class Hub : IAsyncDisposable
{
    public const int DefaultPort = 5090;
    public const int DiscoveryPort = 47870;
    public const string DiscoveryMessage = "ADEN-DISCOVER/1";
    /// <summary>Bu süre içinde istek gelen cihaz bağlı sayılır (istemciler 3 saniyede bir eşitler).</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(15);

    private readonly Store store;
    private WebApplication? app;
    private UdpClient? udp;
    private CancellationTokenSource? stopping;
    private readonly ConcurrentDictionary<string, bool> verified = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTime Until)> failures = new();

    public Hub(Store store) => this.store = store;

    public bool Running => app is not null;
    public string Address { get; private set; } = "";
    public int Port { get; private set; }
    public string Id => store.Setting("hubId");
    public string Name => store.Setting("hubName", Environment.MachineName);
    public string? Error { get; private set; }

    public static string Version =>
        typeof(Hub).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Ayarlardaki adres ve portta yayına başlar.</summary>
    public async Task StartAsync()
    {
        await StopAsync();
        Error = null;
        Address = store.Setting("hubAddress", "0.0.0.0");
        Port = int.TryParse(store.Setting("hubPort"), out var port) ? port : DefaultPort;
        verified.Clear();
        try
        {
            if (!IPAddress.TryParse(Address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                throw new InvalidOperationException("Geçerli bir IPv4 adresi gir.");
            if (!ip.Equals(IPAddress.Any) && !IPAddress.IsLoopback(ip) && !LocalAddresses().Contains(ip.ToString()))
                throw new InvalidOperationException($"{Address} bu bilgisayarın ağ kartlarında yok. Router'da bu cihaza verdiğin sabit IP'yi kontrol et.");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(ip, Port);
                options.Limits.MaxRequestBodySize = 16 * 1024 * 1024;
            });
            var web = builder.Build();
            Map(web);
            await web.StartAsync();
            app = web;
            stopping = new CancellationTokenSource();
            StartDiscovery(stopping.Token);
            store.Log("Sistem", "", Environment.MachineName, $"{Address}:{Port}", "Merkezi sunucu yayına başladı");
        }
        catch (Exception error)
        {
            Error = error is IOException or SocketException || error.InnerException is SocketException
                ? $"{Address}:{Port} dinlenemedi. Port başka bir programda kullanılıyor olabilir."
                : error.Message;
            store.Log("Hata", "", Environment.MachineName, $"{Address}:{Port}", "Merkezi sunucu başlatılamadı", Error, 500);
            await StopAsync();
        }
    }

    public async Task StopAsync()
    {
        stopping?.Cancel();
        udp?.Dispose();
        udp = null;
        if (app is not null)
        {
            var web = app;
            app = null;
            try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await web.StopAsync(timeout.Token); } catch { }
            await web.DisposeAsync();
            store.Log("Sistem", "", Environment.MachineName, $"{Address}:{Port}", "Merkezi sunucu durduruldu");
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private void Map(WebApplication web)
    {
        web.Use(async (context, next) =>
        {
            var watch = Stopwatch.StartNew();
            var address = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "";
            var device = Header(context, "X-Aden-Device");
            var deviceName = Header(context, "X-Aden-Device-Name");
            var user = Header(context, "X-Aden-User");
            var path = context.Request.Path.Value ?? "";
            if (path.StartsWith("/sync", StringComparison.Ordinal))
            {
                if (failures.TryGetValue(address, out var failure) && failure.Count >= 5 && failure.Until > DateTime.UtcNow)
                {
                    context.Response.StatusCode = 429;
                    return;
                }
                if (!Authorized(context))
                {
                    var count = failures.TryGetValue(address, out var previous) && previous.Until > DateTime.UtcNow ? previous.Count + 1 : 1;
                    failures[address] = (count, DateTime.UtcNow.AddMinutes(1));
                    context.Response.StatusCode = 401;
                    store.Log("Hata", user, deviceName, address, $"{context.Request.Method} {path}",
                        "Sunucu kullanıcı adı veya şifresi hatalı.", 401, (int)watch.ElapsedMilliseconds);
                    return;
                }
                failures.TryRemove(address, out _);
                if (device.Length > 0) store.Seen(device, deviceName.Length > 0 ? deviceName : address, address, user);
            }
            try
            {
                await next(context);
            }
            catch (Exception error) when (!context.RequestAborted.IsCancellationRequested)
            {
                context.Response.StatusCode = 500;
                store.Log("Hata", user, deviceName, address, $"{context.Request.Method} {path}", error.Message, 500,
                    (int)watch.ElapsedMilliseconds);
                return;
            }
            if (context.Items.TryGetValue("log", out var detail) && detail is string text)
                store.Log(context.Items["logKind"] as string ?? "İstek", user, deviceName, address,
                    $"{context.Request.Method} {path}", text, context.Response.StatusCode, (int)watch.ElapsedMilliseconds);
        });

        web.MapGet("/health", () => Results.Ok(new { id = Id }));
        web.MapGet("/hub/info", () => Results.Ok(Announcement(Address)));
        web.MapGet("/sync/ping", () => Results.Ok(new { id = Id, name = Name }));
        web.MapPost("/sync/push", (Entry[] entries, HttpContext context) =>
        {
            if (entries.Length > 100 || entries.Any(entry => !EntryValidation.IsValid(entry)))
            {
                context.Items["log"] = "Geçersiz kayıt gönderildi.";
                context.Items["logKind"] = "Hata";
                return Results.BadRequest();
            }
            if (!store.Accept(entries))
            {
                context.Items["log"] = "Aynı kimlikle farklı içerik (çakışma).";
                context.Items["logKind"] = "Hata";
                return Results.Conflict();
            }
            context.Items["log"] = $"{entries.Length} kayıt alındı: " + Describe(entries);
            context.Items["logKind"] = "İşlem";
            return Results.Ok();
        });
        web.MapGet("/sync/pull", (long after, HttpContext context) =>
        {
            var page = store.Page(after);
            // Boş yoklamalar loglanmaz (her cihaz 3 saniyede bir sorar).
            if (page.Changes.Length > 0) context.Items["log"] = $"{page.Changes.Length} kayıt gönderildi.";
            return page;
        });
        web.MapPost("/sync/login", (LoginRequest request, HttpContext context) =>
        {
            var result = Users.Login(store.ReadKind("user"), request.Username ?? "", request.Password ?? "");
            context.Items["logKind"] = result is null ? "Hata" : "Giriş";
            context.Items["log"] = result is null
                ? $"Başarısız giriş denemesi: {Users.Normalize(request.Username ?? "")}"
                : $"{result.Name} ({result.Username}) giriş yaptı.";
            if (result is not null && Header(context, "X-Aden-Device") is { Length: > 0 } device)
                store.SetDeviceUser(device, result.Username);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
        web.MapPost("/sync/logout", (HttpContext context) =>
        {
            context.Items["logKind"] = "Çıkış";
            context.Items["log"] = $"{Header(context, "X-Aden-User")} çıkış yaptı.";
            if (Header(context, "X-Aden-Device") is { Length: > 0 } device) store.SetDeviceUser(device, "");
            return Results.Ok();
        });
        web.MapGet("/sync/logs", (string? from, string? to, string? kind, string? user, string? device, string? search) =>
            store.Logs(ParseTime(from), ParseTime(to), kind ?? "", user ?? "", device ?? "", search ?? ""));
    }

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var time) ? time : null;

    private static string Describe(IEnumerable<Entry> entries) =>
        string.Join(", ", entries.GroupBy(e => e.Kind).Select(g => $"{KindName(g.Key)} ×{g.Count()}"));

    public static string KindName(string kind) => kind switch
    {
        "product" => "Ürün",
        "category" => "Kategori",
        "account" => "Cari",
        "stock" => "Stok hareketi",
        "sale" => "Satış",
        "quote" => "Teklif",
        "quote-revision" => "Teklif güncelleme",
        "cash" => "Kasa hareketi",
        "user" => "Kullanıcı",
        "variation" => "Varyasyon",
        "stock-revision" => "Stok düzeltme",
        "sale-revision" => "Satış düzenleme",
        "return" => "İade / değişim",
        "supplier-return" => "Tedarikçi iadesi",
        _ => kind
    };

    private static string Header(HttpContext context, string name) =>
        Uri.UnescapeDataString(context.Request.Headers[name].ToString());

    private bool Authorized(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.Ordinal)) return false;
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(header + "|" + store.Setting("hubHash"))));
        if (verified.ContainsKey(cacheKey)) return true;
        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..])); }
        catch (FormatException) { return false; }
        var separator = decoded.IndexOf(':');
        if (separator < 0) return false;
        var ok = Users.Normalize(decoded[..separator]) == store.Setting("hubUser") &&
            Users.Verify(decoded[(separator + 1)..], store.Setting("hubHash"), store.Setting("hubSalt"));
        if (ok) verified[cacheKey] = true;
        return ok;
    }

    private HubAnnouncement Announcement(string address) => new(Id, Name, address, Port, Version);

    // ───────────── UDP keşif ─────────────

    private void StartDiscovery(CancellationToken token)
    {
        try
        {
            var socket = new UdpClient(AddressFamily.InterNetwork);
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            udp = socket;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var request = await socket.ReceiveAsync(token);
                        if (Encoding.UTF8.GetString(request.Buffer) != DiscoveryMessage) continue;
                        var address = Address == "0.0.0.0" ? AddressFor(request.RemoteEndPoint.Address) : Address;
                        var reply = JsonSerializer.SerializeToUtf8Bytes(Announcement(address));
                        await socket.SendAsync(reply, request.RemoteEndPoint, token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (SocketException) { }
                }
            }, token);
        }
        catch (SocketException error)
        {
            store.Log("Hata", "", Environment.MachineName, $"UDP {DiscoveryPort}", "Ağ keşfi başlatılamadı", error.Message, 500);
        }
    }

    /// <summary>Tüm arayüzlerde dinlerken, isteği gönderen cihazla aynı alt ağdaki adresimizi seçer.</summary>
    private static string AddressFor(IPAddress remote)
    {
        if (IPAddress.IsLoopback(remote)) return "127.0.0.1";
        var bytes = remote.MapToIPv4().GetAddressBytes();
        var locals = LocalAddresses();
        return locals.FirstOrDefault(local =>
        {
            var parts = IPAddress.Parse(local).GetAddressBytes();
            return parts[0] == bytes[0] && parts[1] == bytes[1] && parts[2] == bytes[2];
        }) ?? locals.FirstOrDefault() ?? "127.0.0.1";
    }

    /// <summary>Bu bilgisayarın çalışan ağ kartlarındaki IPv4 adresleri.</summary>
    public static List<string> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString()).Distinct().ToList();

    /// <summary>Yerel ağdaki merkezi sunucuları UDP yayınıyla bulur.</summary>
    public static async Task<List<HubAnnouncement>> ScanAsync(TimeSpan duration, CancellationToken token = default)
    {
        var found = new Dictionary<string, HubAnnouncement>();
        using var socket = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        socket.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        var message = Encoding.UTF8.GetBytes(DiscoveryMessage);
        var targets = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            foreach (var unicast in network.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && a.IPv4Mask is not null))
            {
                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                targets.Add(new IPAddress(ip.Select((b, i) => (byte)(b | ~mask[i])).ToArray()));
            }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(duration);
        for (var attempt = 0; attempt < 2; attempt++)
            foreach (var target in targets.Distinct())
                try { await socket.SendAsync(message, new IPEndPoint(target, DiscoveryPort), timeout.Token); }
                catch (SocketException) { }
        try
        {
            while (true)
            {
                var reply = await socket.ReceiveAsync(timeout.Token);
                try
                {
                    var hub = JsonSerializer.Deserialize<HubAnnouncement>(reply.Buffer);
                    if (hub is null || hub.Id.Length == 0) continue;
                    // Aynı makinedeki sunucu hem geri döngü hem LAN adresinden yanıt verebilir; LAN adresini tercih et.
                    if (!found.TryGetValue(hub.Id, out var known) || known.Address == "127.0.0.1") found[hub.Id] = hub;
                }
                catch (JsonException) { }
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        return found.Values.OrderBy(h => h.Name).ToList();
    }
}

/// <summary>Seçili çalışma moduna göre (uzak sunucu veya merkezi yerel sunucu) yapılandırılmış HTTP istemcisi.</summary>
public static class Connection
{
    public static bool IsNetworked(Store store) => store.Setting("mode") is "sync" or "client";

    public static string Describe(Store store) => store.Setting("mode") switch
    {
        "sync" => "Uzak sunucu · " + store.Setting("url"),
        "client" => $"Merkezi sunucu · {store.Setting("hubName", "Yerel sunucu")} ({store.Setting("clientAddress")}:{store.Setting("clientPort")})",
        "hub" => "Merkezi sunucu (bu cihaz)",
        _ => "Yerel mod · Veriler bu bilgisayarda"
    };

    public static HttpClient? Create(Store store, string user, TimeSpan timeout)
    {
        var mode = store.Setting("mode");
        HttpClient http;
        if (mode == "sync")
        {
            http = new HttpClient { BaseAddress = new Uri(store.Setting("url") + "/"), Timeout = timeout };
            http.DefaultRequestHeaders.Add("X-Aden-Key", store.Setting("key"));
        }
        else if (mode == "client")
        {
            http = Client(store.Setting("clientAddress"), int.Parse(store.Setting("clientPort", Hub.DefaultPort.ToString())),
                store.Setting("clientUser"), Secrets.Unprotect(store.Setting("clientPassword")), timeout);
        }
        else return null;
        AddIdentity(http, store, user);
        return http;
    }

    public static HttpClient Client(string address, int port, string user, string password, TimeSpan timeout)
    {
        var http = new HttpClient { BaseAddress = new Uri($"http://{address}:{port}/"), Timeout = timeout };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Users.Normalize(user) + ":" + password)));
        return http;
    }

    public static void AddIdentity(HttpClient http, Store store, string user)
    {
        http.DefaultRequestHeaders.Add("X-Aden-Device", store.Setting("deviceId"));
        http.DefaultRequestHeaders.Add("X-Aden-Device-Name", Uri.EscapeDataString(Environment.MachineName));
        if (user.Length > 0) http.DefaultRequestHeaders.Add("X-Aden-User", Uri.EscapeDataString(user));
    }
}

/// <summary>Bu Windows kullanıcısına bağlı (DPAPI) şifreleme; cihaz şifresi diske düz metin yazılmaz.</summary>
public static class Secrets
{
    public static string Protect(string plain) => plain.Length == 0 ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser)); }
        catch (Exception error) when (error is CryptographicException or FormatException) { return ""; }
    }
}
