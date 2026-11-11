CREATE TABLE IF NOT EXISTS logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    at TEXT NOT NULL,
    kind TEXT NOT NULL,
    user TEXT NOT NULL,
    device TEXT NOT NULL,
    address TEXT NOT NULL,
    action TEXT NOT NULL,
    detail TEXT NOT NULL,
    status INTEGER NOT NULL,
    duration INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS logs_at ON logs(at);
CREATE TABLE IF NOT EXISTS devices (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    address TEXT NOT NULL,
    user TEXT NOT NULL,
    first_seen TEXT NOT NULL,
    last_seen TEXT NOT NULL,
    requests INTEGER NOT NULL
);
