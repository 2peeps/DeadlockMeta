-- One row per install and crosshair for "apply" and "like", so repeats from one install do not count.
CREATE TABLE IF NOT EXISTS events (
  id TEXT NOT NULL,
  install TEXT NOT NULL,
  kind TEXT NOT NULL,
  at INTEGER NOT NULL,
  PRIMARY KEY (id, install, kind)
);
CREATE INDEX IF NOT EXISTS events_by_id ON events (id, kind);

-- Crosshairs players shared from the app.
CREATE TABLE IF NOT EXISTS shared (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  code TEXT NOT NULL UNIQUE,
  install TEXT NOT NULL,
  created INTEGER NOT NULL,
  hidden INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS shared_by_install ON shared (install, created);
