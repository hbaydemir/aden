using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aden;

/// <summary>Uygulama kullanıcısı. Şifre PBKDF2-SHA256 özeti olarak saklanır, düz metin asla yazılmaz.</summary>
public record UserAccount(string Username, string Name, string Role, string Hash, string Salt, bool Archived = false,
    bool MustChange = false);

public record LoginRequest(string Username, string Password);
public record LoginResult(string Username, string Name, string Role, bool MustChange);
/// <summary>Yerel ağ keşfi yanıtı: merkezi sunucunun kimliği ve adresi.</summary>
public record HubAnnouncement(string Id, string Name, string Address, int Port, string Version);
public record UsageLog(long Id, DateTimeOffset At, string Kind, string User, string Device, string Address,
    string Action, string Detail, int Status, int Duration);

public static class Users
{
    public const string Admin = "admin";
    public const string AdminRole = "Yönetici";
    public const string UserRole = "Kullanıcı";
    public const string DefaultAdminPassword = "admin";
    public const string Developer = "dev";
    private const int Iterations = 120_000;
    // Geliştirici şifresinin özeti (düz metin kodda tutulmaz).
    private const string DeveloperSalt = "YWRlbi1kZXZlbG9wZXItc2FsdA==";
    private const string DeveloperHash = "It5WxTBm5+NNME3Im2otcu1pN9K0pinxpdzVJN643pc=";

    public static UserAccount Create(string username, string name, string role, string password, bool mustChange = false)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new(Normalize(username), name.Trim(), role, Convert.ToBase64String(Derive(password, salt)),
            Convert.ToBase64String(salt), false, mustChange);
    }

    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    public static bool Verify(UserAccount user, string password) =>
        Verify(password, user.Hash, user.Salt);

    public static bool Verify(string password, string hash, string salt)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(hash),
                Derive(password, Convert.FromBase64String(salt)));
        }
        catch (FormatException) { return false; }
    }

    public static (string Hash, string Salt) HashSecret(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return (Convert.ToBase64String(Derive(secret, salt)), Convert.ToBase64String(salt));
    }

    public static bool IsDeveloper(string username, string password) =>
        Normalize(username) == Developer && Verify(password, DeveloperHash, DeveloperSalt);

    private static byte[] Derive(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Kayıtlardan güncel kullanıcı listesi: aynı kullanıcı adının en son kaydı geçerlidir.
    /// Hiç "admin" kaydı yoksa varsayılan yönetici (admin/admin, ilk girişte şifre değişir) kullanılır.</summary>
    public static Dictionary<string, UserAccount> Resolve(IEnumerable<Entry> entries)
    {
        var users = new Dictionary<string, UserAccount>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(e => e.Kind == "user").OrderBy(e => e.CreatedAt).ThenBy(e => e.Id))
        {
            var user = JsonSerializer.Deserialize<UserAccount>(entry.Data);
            if (user is not null) users[Normalize(user.Username)] = user;
        }
        if (!users.ContainsKey(Admin)) users[Admin] = DefaultAdmin;
        foreach (var archived in users.Where(pair => pair.Value.Archived).Select(pair => pair.Key).ToArray())
            users.Remove(archived);
        return users;
    }

    private static readonly Lazy<UserAccount> defaultAdmin = new(() =>
        Create(Admin, "Yönetici", AdminRole, DefaultAdminPassword, true));
    public static UserAccount DefaultAdmin => defaultAdmin.Value;

    /// <summary>Kullanıcı adı ve şifreyi kayıtlara göre doğrular.</summary>
    public static LoginResult? Login(IEnumerable<Entry> entries, string username, string password)
    {
        var name = Normalize(username);
        if (name == Developer) return null;
        if (!Resolve(entries).TryGetValue(name, out var user) || !Verify(user, password)) return null;
        return new(user.Username, user.Name, user.Role, user.MustChange);
    }

    public static bool ValidUsername(string username) =>
        username.Length is >= 3 and <= 32 && username.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
