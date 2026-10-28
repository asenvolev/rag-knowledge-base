-- Runs once, on the first start of an empty Postgres volume.
-- Tables and indexes are created by the API at startup (DatabaseInitializer.cs),
-- so this only needs to enable the extension.
CREATE EXTENSION IF NOT EXISTS vector;
