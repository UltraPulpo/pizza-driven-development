-- Applied at API startup by DatabaseInitializer.
-- Every statement must be idempotent (e.g. CREATE TABLE IF NOT EXISTS).

CREATE TABLE IF NOT EXISTS Orders (
    id TEXT NOT NULL PRIMARY KEY,
    submission_key TEXT NOT NULL UNIQUE,
    submission_request_json TEXT NOT NULL,
    submission_receipt_json TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('NotStarted', 'Started', 'ReadyForPickup', 'Delivered', 'Cancelled')),
    order_json TEXT NOT NULL
);
