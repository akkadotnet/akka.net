// -----------------------------------------------------------------------
//  <copyright file="SqliteJournal.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Data.Common;
using Akka.Configuration;
using Akka.Persistence.Sql.Common.Journal;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Sqlite.Journal;

/// <summary>
///     TBD
/// </summary>
public class SqliteJournal : SqlJournal
{
    /// <summary>
    ///     TBD
    /// </summary>
    public static readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="journalConfig">TBD</param>
    public SqliteJournal(Config journalConfig) : base(journalConfig.WithFallback(Extension.DefaultJournalConfig))
    {
        var config = journalConfig.WithFallback(Extension.DefaultJournalConfig);
        QueryExecutor = new SqliteQueryExecutor(new QueryConfiguration(
                null,
                config.GetString("table-name"),
                config.GetString("metadata-table-name"),
                "persistence_id",
                "sequence_nr",
                "payload",
                "manifest",
                "timestamp",
                "is_deleted",
                "tags",
                "ordering",
                "serializer_id",
                config.GetTimeSpan("connection-timeout"),
                config.GetString("serializer"),
                config.GetBoolean("use-sequential-access"),
                Settings.ReadIsolationLevel,
                Settings.WriteIsolationLevel),
            Context.System.Serialization,
            GetTimestampProvider(config.GetString("timestamp-provider")));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public override IJournalQueryExecutor QueryExecutor { get; }

    /// <summary>
    ///     TBD
    /// </summary>
    protected override string JournalConfigPath => SqlitePersistence.JournalConfigPath;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="connectionString">TBD</param>
    /// <returns>TBD</returns>
    protected override DbConnection CreateDbConnection(string connectionString)
    {
        return new SqliteConnection(connectionString);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    protected override void PreStart()
    {
        ConnectionContext.Remember(GetConnectionString());
        base.PreStart();
    }

    /// <summary>
    ///     TBD
    /// </summary>
    protected override void PostStop()
    {
        base.PostStop();
        ConnectionContext.Forget(GetConnectionString());
    }
}