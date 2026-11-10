using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aden;

public sealed class Store : IDisposable
{
    static Store() => SQLitePCL.Batteries_V2.Init();

    private readonly SqliteConnection db;

    /// <summary>Veritabanının bulunduğu klasör (fotoğraflar burada saklanır).</summary>
    public string Folder { get; }

    public Store(string path)
    {
        const int currentSchema = 1;
        path = Path.GetFullPath(path);
        Folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
            if (schema == currentSchema) return;
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
        catch
        {
            db.Dispose();
            throw;
        }
    }

    private void Backup(string path, int schema)
    {
        var directory = Path.Combine(Path.GetDirectoryName(path)!, "Yedekler");
        Directory.CreateDirectory(directory);
        var name = $"{Path.GetFileNameWithoutExtension(path)}-v{schema}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db";
        var target = Path.Combine(directory, name);
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
        using var command = db.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE name=$0";
        command.Parameters.AddWithValue("$0", name);
        return command.ExecuteScalar() as string ?? fallback;
    }

    public void SaveSyncSettings(string mode, string url, string key)
    {
        var previous = Setting("url");
        using var tx = db.BeginTransaction();
        if (mode == "sync")
        {
            if (previous.Length > 0 && previous != url)
                throw new InvalidOperationException("Bu veritabanı başka bir sunucuya bağlı.");
            SaveSetting("url", url, tx);
            SaveSetting("key", key, tx);
        }
        SaveSetting("mode", mode, tx);
        tx.Commit();
    }

    public void Add<T>(string kind, T data)
    {
        var entry = new Entry(Guid.NewGuid(), kind, DateTimeOffset.UtcNow, JsonSerializer.Serialize(data));
        if (!EntryValidation.IsValid(entry))
            throw new InvalidOperationException("Kayıt geçersiz veya tek işlem için fazla büyük.");
        Insert(entry, false, null);
    }

    public List<Entry> Read(bool pendingOnly = false)
    {
        using var command = db.CreateCommand();
        command.CommandText = pendingOnly
            ? "SELECT id,kind,created_at,data FROM entries WHERE synced=0 ORDER BY position LIMIT 100"
            : "SELECT id,kind,created_at,data FROM entries ORDER BY created_at,id";
        using var reader = command.ExecuteReader();
        var entries = new List<Entry>();
        while (reader.Read())
            entries.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture), reader.GetString(3)));
        return entries;
    }

    public void Acknowledge(IEnumerable<Entry> entries)
    {
        using var tx = db.BeginTransaction();
        foreach (var entry in entries)
            Execute("UPDATE entries SET synced=1 WHERE id=$0", tx, entry.Id.ToString());
        tx.Commit();
    }

    public void Import(SyncPage page)
    {
        using var tx = db.BeginTransaction();
        foreach (var change in page.Changes)
            Insert(change.Entry, true, tx);
        SaveSetting("cursor", page.Cursor.ToString(CultureInfo.InvariantCulture), tx);
        tx.Commit();
    }

    public void SaveSetting(string name, string value) => SaveSetting(name, value, null);

    private void SaveSetting(string name, string value, SqliteTransaction? tx) =>
        Execute("INSERT INTO settings VALUES ($0,$1) ON CONFLICT(name) DO UPDATE SET value=excluded.value",
            tx, name, value);

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

    public void Dispose() => db.Dispose();
}




