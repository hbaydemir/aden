using System.Security.Cryptography;
using System.Text;
using Aden;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("Aden")
    ?? throw new InvalidOperationException("ConnectionStrings:Aden gerekli.");
var key = builder.Configuration["Aden:SyncKey"];
if (string.IsNullOrWhiteSpace(key) || key.Length < 24)
    throw new InvalidOperationException("En az 24 karakterlik eşitleme anahtarı gerekli.");

await using var db = NpgsqlDataSource.Create(connection);
await using (var conn = await db.OpenConnectionAsync())
await using (var transaction = await conn.BeginTransactionAsync())
{
    await using var setup = new NpgsqlCommand("""
        SELECT pg_advisory_xact_lock(720062);
        CREATE TABLE IF NOT EXISTS schema_migrations (version integer PRIMARY KEY);
        """, conn, transaction);
    await setup.ExecuteNonQueryAsync();
    await using var version = new NpgsqlCommand(
        "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version=1)", conn, transaction);
    if (await version.ExecuteScalarAsync() is false)
    {
        await using var migration = new NpgsqlCommand(ReadMigration(), conn, transaction);
        await migration.ExecuteNonQueryAsync();
        await using var record = new NpgsqlCommand("INSERT INTO schema_migrations VALUES (1)", conn, transaction);
        await record.ExecuteNonQueryAsync();
    }
    await transaction.CommitAsync();
}

var app = builder.Build();
var expectedKey = SHA256.HashData(Encoding.UTF8.GetBytes(key));
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/sync") &&
        !CryptographicOperations.FixedTimeEquals(expectedKey,
            SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Aden-Key"].ToString()))))
    {
        context.Response.StatusCode = 401;
        return;
    }

    await next(context);
});
app.MapGet("/health", async (CancellationToken ct) =>
{
    await using var command = db.CreateCommand("SELECT 1");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok();
});
app.MapPost("/sync/push", async (Entry[] entries, CancellationToken ct) =>
{
    if (entries.Length > 100 || entries.Any(entry => !EntryValidation.IsValid(entry)))
        return Results.BadRequest();

    await using var conn = await db.OpenConnectionAsync(ct);
    await using var transaction = await conn.BeginTransactionAsync(ct);
    // Serialize sequence assignment through commit so a pull cursor cannot skip a later commit.
    await using (var gate = new NpgsqlCommand("SELECT pg_advisory_xact_lock(720061)", conn, transaction))
        await gate.ExecuteNonQueryAsync(ct);
    foreach (var entry in entries)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO entries(id, kind, created_at, data) VALUES ($1,$2,$3,$4)
            ON CONFLICT (id) DO UPDATE SET id=entries.id
            WHERE entries.kind=excluded.kind AND entries.created_at=excluded.created_at AND entries.data=excluded.data
            """, conn, transaction);
        insert.Parameters.AddWithValue(entry.Id);
        insert.Parameters.AddWithValue(entry.Kind);
        insert.Parameters.AddWithValue(entry.CreatedAt.ToUniversalTime());
        insert.Parameters.AddWithValue(entry.Data);
        if (await insert.ExecuteNonQueryAsync(ct) != 1)
            return Results.Conflict();
    }
    await transaction.CommitAsync(ct);
    return Results.Ok();
});
app.MapGet("/sync/pull", async (long after, CancellationToken ct) =>
{
    var changes = new List<Change>();
    await using var command = db.CreateCommand("""
        SELECT position,id,kind,created_at,data FROM entries WHERE position>$1 ORDER BY position LIMIT 100
        """);
    command.Parameters.AddWithValue(after);
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
        changes.Add(new(reader.GetInt64(0), new(reader.GetGuid(1), reader.GetString(2),
            new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero), reader.GetString(4))));
    return new SyncPage(changes.Count == 0 ? after : changes[^1].Sequence, changes.ToArray());
});
await app.RunAsync();

static string ReadMigration()
{
    using var stream = typeof(Program).Assembly.GetManifestResourceStream("Aden.Server.Migrations.001_initial.sql")!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

