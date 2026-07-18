# Copilot Instructions

## Project Guidelines
- For the OfflineAI project's file-agent slash commands, /läs must always take the form "/läs <filnamn> <instruktion>" (an explicit instruction is required alongside the filename) — never treat a command as just forwarding raw file content as the prompt with no instruction. This mirrors the existing /fyll <filnamn> <beskrivning> pattern.

## Database Management
- In the OfflineAI project, when adding schema changes to Dapper repositories (e.g., Infrastructure.Data.Dapper), prefer additive migrations inside InitializeDatabaseAsync (CREATE TABLE for new DBs + ALTER TABLE/column-existence check for existing DBs) rather than requiring manual migration scripts, so the app self-heals its schema on startup.