CREATE TABLE IF NOT EXISTS entries (
    position bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id uuid NOT NULL UNIQUE,
    kind text NOT NULL,
    created_at timestamptz NOT NULL,
    data text NOT NULL
);
