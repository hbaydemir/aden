using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aden;

public record DeviceInfo(string Id, string Name, string Address, string User, DateTimeOffset FirstSeen, DateTimeOffset LastSeen,
    long Requests);

public sealed class Store : IDisposable
{
    static Store() => SQLitePCL.Batteries_V2.Init();

    private readonly SqliteConnection db;
    // Merkezi sunucu modunda istekler arka plan iş parçacıklarından gelir; tek bağlantı bu kilitle sıraya alınır.
    private readonly object gate = new();

    /// <summary>Veritabanının bulunduğu klasör (fotoğraflar burada saklanır).</summary>
    public string Folder { get; }
    public string Path { get; }

    /// <summary>Başka bir cihazdan gelen kayıtlar eklendiğinde tetiklenir (arka plan iş parçacığında).</summary>
    public event Action? Changed;

    public Store(string path)
    {
        const int currentSchema = 2;
        path = System.IO.Path.GetFullPath(path);
        Path = path;
        Folder = System.IO.Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(Folder);
        var existing = File.Exists(path) && new FileInfo(path).Length > 0;
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        try
        {
            db.Open();
            using var version = db.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            var schema = Convert.ToInt32(version.ExecuteScalar());
            if (schema > currentSchema)
                throw new InvalidOperationException("Veritabanı bu Aden sürümünden daha yeni.");
            if (schema < currentSchema)
            {
                if (existing) Backup(path, schema);
                using var transaction = db.BeginTransaction();
                for (var next = schema + 1; next <= currentSchema; next++)
                {
                    var name = typeof(Store).Assembly.GetManifestResourceNames()
                        .Single(resource => resource.StartsWith($"Aden.Desktop.Migrations.{next:000}_", StringComparison.Ordinal));
                    using var stream = typeof(Store).Assembly.GetManifestResourceStream(name)!;
                    using var reader = new StreamReader(stream);
                    Execute(reader.ReadToEnd(), transaction);
                    Execute($"PRAGMA user_version={next}", transaction);
                }
                transaction.Commit();
            }
            if (Setting("deviceId").Length == 0) SaveSetting("deviceId", Guid.NewGuid().ToString("N"));
            // Kullanım logları 90 gün saklanır.
            Execute("DELETE FROM logs WHERE at<$0", null, DateTimeOffset.UtcNow.AddDays(-90).ToString("O"));
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private void Backup(string path, int schema)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "Yedekler");
        Directory.CreateDirectory(directory);
        var name = $"{System.IO.Path.GetFileNameWithoutExtension(path)}-v{schema}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db";
        var target = System.IO.Path.Combine(directory, name);
        var temporary = target + ".tmp";
        try
        {
            using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = temporary, Pooling = false }.ToString()))
            {
                copy.Open();
                db.BackupDatabase(copy);
            }
            File.Move(temporary, target);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public string Setting(string name, string fallback = "")
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE name=$0";
            command.Parameters.AddWithValue("$0", name);
            return command.ExecuteScalar() as string ?? fallback;
        }
    }

    /// <summary>Çalışma modunu kaydeder. mode: local, sync (uzak sunucu), hub (merkezi yerel sunucu), client (merkezi sunucuya bağlı).
    /// server: bağlanılan sunucunun kimliği; başka bir sunucunun verisiyle karışmasın diye değişemez.</summary>
    public void SaveConnection(string mode, string server, IReadOnlyDictionary<string, string> values)
    {
        lock (gate)
        {
            var previous = Setting("server");
            if (previous.Length == 0 && Setting("url").Length > 0) previous = Setting("url");
            using var tx = db.BeginTransaction();
            if (mode is "sync" or "client")
            {
                if (previous.Length > 0 && previous != server)
                    throw new InvalidOperationException("Bu veritabanı daha önce başka bir sunucuya bağlandı. Yeni sunucu için temiz bir veritabanı kullan.");
                SaveSetting("server", server, tx);
                // Bu cihazda bağlanmadan önce oluşturulan kullanıcılar merkezdeki kullanıcıları ezmesin.
                Execute("DELETE FROM entries WHERE kind='user' AND synced=0", tx);
            }
            foreach (var (name, value) in values) SaveSetting(name, value, tx);
            SaveSetting("mode", mode, tx);
            tx.Commit();
        }
    }

    public void Add<T>(string kind, T data) => Add(kind, data, Guid.NewGuid(), DateTimeOffset.UtcNow);

    /// <summary>Belirli kimlik ve tarihle kayıt ekler (içe aktarma). Aynı kimlik varsa eklenmez.</summary>
    public void Add<T>(string kind, T data, Guid id, DateTimeOffset at)
    {
        var entry = new Entry(id, kind, at, JsonSerializer.Serialize(data));
        if (!EntryValidation.IsValid(entry))
            throw new InvalidOperationException("Kayıt geçersiz veya tek işlem için fazla büyük.");
        lock (gate) Insert(entry, false, null);
    }

    public List<Entry> Read(bool pendingOnly = false)
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = pendingOnly
                ? "SELECT id,kind,created_at,data FROM entries WHERE synced=0 ORDER BY position LIMIT 100"
                : "SELECT id,kind,created_at,data FROM entries ORDER BY created_at,id";
            using var reader = command.ExecuteReader();
            var entries = new List<Entry>();
            while (reader.Read()) entries.Add(ReadEntry(reader, 0));
            return entries;
        }
    }

    public List<Entry> ReadKind(string kind)
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = "SELECT id,kind,created_at,data FROM entries WHERE kind=$0 ORDER BY created_at,id";
            command.Parameters.AddWithValue("$0", kind);
            using var reader = command.ExecuteReader();
            var entries = new List<Entry>();
            while (reader.Read()) entries.Add(ReadEntry(reader, 0));
            return entries;
        }
    }

    private static Entry ReadEntry(SqliteDataReader reader, int start) =>
        new(Guid.Parse(reader.GetString(start)), reader.GetString(start + 1),
            DateTimeOffset.Parse(reader.GetString(start + 2), CultureInfo.InvariantCulture), reader.GetString(start + 3));

    /// <summary>Merkezi sunucu: sıra numarasından sonraki kayıtlar (en fazla 100).</summary>
    public SyncPage Page(long after)
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = "SELECT position,id,kind,created_at,data FROM entries WHERE position>$0 ORDER BY position LIMIT 100";
            command.Parameters.AddWithValue("$0", after);
            using var reader = command.ExecuteReader();
            var changes = new List<Change>();
            while (reader.Read()) changes.Add(new(reader.GetInt64(0), ReadEntry(reader, 1)));
            return new(changes.Count == 0 ? after : changes[^1].Sequence, changes.ToArray());
        }
    }

    /// <summary>Merkezi sunucu: istemciden gelen kayıtları ekler. Aynı kimlikle farklı içerik varsa false döner.</summary>
    public bool Accept(IReadOnlyList<Entry> entries)
    {
        var added = false;
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            foreach (var entry in entries)
            {
                using var check = db.CreateCommand();
                check.Transaction = tx;
                check.CommandText = "SELECT kind,created_at,data FROM entries WHERE id=$0";
                check.Parameters.AddWithValue("$0", entry.Id.ToString());
                using (var reader = check.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        if (reader.GetString(0) != entry.Kind || reader.GetString(2) != entry.Data ||
                            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture) != entry.CreatedAt)
                            return false;
                        continue;
                    }
                }
                Insert(entry, false, tx);
                added = true;
            }
            tx.Commit();
        }
        if (added) Changed?.Invoke();
        return true;
    }

    public void Acknowledge(IEnumerable<Entry> entries)
    {
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            foreach (var entry in entries)
                Execute("UPDATE entries SET synced=1 WHERE id=$0", tx, entry.Id.ToString());
            tx.Commit();
        }
    }

    public void Import(SyncPage page)
    {
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            foreach (var change in page.Changes)
                Insert(change.Entry, true, tx);
            SaveSetting("cursor", page.Cursor.ToString(CultureInfo.InvariantCulture), tx);
            tx.Commit();
        }
    }

    public void SaveSetting(string name, string value)
    {
        lock (gate) SaveSetting(name, value, null);
    }

    private void SaveSetting(string name, string value, SqliteTransaction? tx) =>
        Execute("INSERT INTO settings VALUES ($0,$1) ON CONFLICT(name) DO UPDATE SET value=excluded.value",
            tx, name, value);

    // ───────────── Kullanım logları ─────────────

    public void Log(string kind, string user, string device, string address, string action, string detail = "",
        int status = 200, int duration = 0)
    {
        try
        {
            lock (gate)
                Execute("""
                    INSERT INTO logs(at,kind,user,device,address,action,detail,status,duration) VALUES ($0,$1,$2,$3,$4,$5,$6,$7,$8)
                    """, null, DateTimeOffset.UtcNow.ToString("O"), kind, user, device, address, Clip(action, 300),
                    Clip(detail, 2000), status, duration);
        }
        catch (SqliteException) { }
    }

    private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];

    public List<UsageLog> Logs(DateTimeOffset? from, DateTimeOffset? to, string kind, string user, string device,
        string search, int limit = 2000)
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            var where = new List<string>();
            void Filter(string sql, object value)
            {
                where.Add(sql.Replace("@", "$" + command.Parameters.Count));
                command.Parameters.AddWithValue("$" + command.Parameters.Count, value);
            }
            if (from is { } start) Filter("at>=@", start.ToUniversalTime().ToString("O"));
            if (to is { } end) Filter("at<@", end.ToUniversalTime().ToString("O"));
            if (kind.Length > 0) Filter("kind=@", kind);
            if (user.Length > 0) Filter("user=@", user);
            if (device.Length > 0) Filter("device=@", device);
            if (search.Length > 0) Filter("(action LIKE @ OR detail LIKE @ OR address LIKE @)", "%" + search + "%");
            command.CommandText = "SELECT id,at,kind,user,device,address,action,detail,status,duration FROM logs" +
                (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + $" ORDER BY id DESC LIMIT {Math.Clamp(limit, 1, 10_000)}";
            using var reader = command.ExecuteReader();
            var logs = new List<UsageLog>();
            while (reader.Read())
                logs.Add(new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.GetString(7), reader.GetInt32(8), reader.GetInt32(9)));
            return logs;
        }
    }

    // ───────────── Bağlı cihazlar (merkezi sunucu) ─────────────

    public void Seen(string id, string name, string address, string user)
    {
        try
        {
            lock (gate)
            {
                var now = DateTimeOffset.UtcNow.ToString("O");
                Execute("""
                    INSERT INTO devices(id,name,address,user,first_seen,last_seen,requests) VALUES ($0,$1,$2,$3,$4,$4,1)
                    ON CONFLICT(id) DO UPDATE SET name=excluded.name, address=excluded.address,
                        user=CASE WHEN excluded.user='' THEN devices.user ELSE excluded.user END,
                        last_seen=excluded.last_seen, requests=devices.requests+1
                    """, null, id, Clip(name, 120), Clip(address, 64), Clip(user, 64), now);
            }
        }
        catch (SqliteException) { }
    }

    public void SetDeviceUser(string id, string user)
    {
        lock (gate) Execute("UPDATE devices SET user=$1 WHERE id=$0", null, id, user);
    }

    public void ForgetDevice(string id)
    {
        lock (gate) Execute("DELETE FROM devices WHERE id=$0", null, id);
    }

    public List<DeviceInfo> Devices()
    {
        lock (gate)
        {
            using var command = db.CreateCommand();
            command.CommandText = "SELECT id,name,address,user,first_seen,last_seen,requests FROM devices ORDER BY last_seen DESC";
            using var reader = command.ExecuteReader();
            var devices = new List<DeviceInfo>();
            while (reader.Read())
                devices.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture), reader.GetInt64(6)));
            return devices;
        }
    }

    private void Insert(Entry entry, bool synced, SqliteTransaction? tx) =>
        Execute("""
            INSERT INTO entries(id,kind,created_at,data,synced) VALUES ($0,$1,$2,$3,$4)
            ON CONFLICT(id) DO NOTHING
            """, tx, entry.Id.ToString(), entry.Kind, entry.CreatedAt.ToUniversalTime().ToString("O"),
            entry.Data, synced ? 1 : 0);

    private void Execute(string sql, SqliteTransaction? tx, params object[] args)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        for (var i = 0; i < args.Length; i++)
            command.Parameters.AddWithValue("$" + i, args[i]);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (gate) db.Dispose();
    }
}
