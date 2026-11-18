# Enabling retry-until-acknowledged on an existing database

The per-reminder `ReminderRetryPolicy.UntilAcknowledged` option uses a stored integer policy:
`0` means `UseConfiguredAttemptLimit` and `1` means `UntilAcknowledged`. Existing reminders keep
bounded delivery when the column is added.

With `AutoInitialize = true`, the provider adds the column when it initializes. **No manual SQL step
is needed.** With `AutoInitialize = false`, an existing database without the column continues to
support bounded reminders. The provider logs a warning linking here and rejects an explicit
`UntilAcknowledged` schedule until the database is upgraded. It never silently converts that request
to bounded delivery.

## Procedure for manually managed databases

1. Upgrade the application binaries on every node that can host the reminder scheduler. Also upgrade
   nodes that receive opted-in scheduling responses or reminder listings. Keep using bounded delivery
   until the entire deployment understands the new policy.
2. Back up the reminder database and stop **every scheduler host and storage provider instance** that
   uses it before applying DDL. This prevents writes during migration and ensures no host continues
   using cached metadata for the old schema.
3. Select the script for the configured provider and review the target names:

   | Provider | Script | Default target | Custom configuration |
   | --- | --- | --- | --- |
   | SQL Server | [sql-server.sql](sql-server.sql) | `[reminders].[scheduled_reminders]` | Set `@SchemaName` and `@TableName` at the top. |
   | PostgreSQL | [postgresql.sql](postgresql.sql) | `"reminders"."scheduled_reminders"` | Replace the quoted schema and table identifiers. |
   | SQLite | [sqlite.sql](sqlite.sql) | `"scheduled_reminders"` | Replace the quoted table identifier and use the configured database file. |

4. For SQLite, check for the column **before** running its script:

   ```sql
   SELECT name FROM pragma_table_info('scheduled_reminders') WHERE name = 'retry_policy';
   ```

   Replace the table name if customized. If the query returns `retry_policy`, skip the SQLite script;
   if it returns no row, execute the script once. SQLite has no `ADD COLUMN IF NOT EXISTS` form. The
   SQL Server and PostgreSQL scripts can be repeated safely without changing stored policy values.
5. Execute the matching script against the reminder database using your usual migration tool. The
   scripts add a non-null integer column with default `0`; they do not delete, re-key, or reschedule
   existing reminders. The reminder table must already exist with the occurrence-based schema.
6. Recreate **every storage provider instance and scheduler host** using the database, including any
   host that can take over later. Schema capability is cached per provider instance. A provider that
   previously saw the old schema must be recreated; merely applying DDL does not refresh its cache.
7. Confirm all these providers now discover the column and the missing-column warning has cleared
   before **any writer** enables `UntilAcknowledged`. A stale provider still using the old projection
   could read another host's opted-in row as bounded work. Then enable the policy on the individual
   one-off reminders that require it. Configured delivery windows and explicit cancellation still apply.

## Compatibility and rollback

Applications using the existing bounded API can keep operating on an old schema with initialization
disabled. Adding the policy column retains bounded behavior for existing rows and legacy inserts that
omit it. It does not require rewriting reminder payloads or changing occurrence identities.

Older scheduler versions ignore the policy column and can exhaust delivery attempts for opted-in
reminders. **Do not let an older scheduler take over while opted-in reminders exist.** Opted-in wire
messages also require upgraded readers. Keep all potential scheduler hosts and policy-message readers
upgraded before enabling the feature; adding the column alone is insufficient.

If rollback to an older scheduler is required, first resolve the active opted-in reminders according
to application policy. Leaving the additive column in place preserves database compatibility, but it
does not make the older scheduler honor retry-until-acknowledged semantics.
