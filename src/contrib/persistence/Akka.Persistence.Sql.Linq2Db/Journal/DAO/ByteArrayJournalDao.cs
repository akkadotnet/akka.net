using System;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Streams;
using LinqToDB;

namespace Akka.Persistence.Sql.Linq2Db.Journal.DAO
{
    public class ByteArrayJournalDao : BaseByteArrayJournalDao
    {
        public ByteArrayJournalDao(IAdvancedScheduler sched, IMaterializer mat,
            AkkaPersistenceDataConnectionFactory connection,
            JournalConfig journalConfig,
            Akka.Serialization.Serialization serializer, ILoggingAdapter logger) : base(sched, mat,
            connection, journalConfig,
            new ByteArrayJournalSerializer(journalConfig, serializer,
                journalConfig.PluginConfig.TagSeparator),logger)
        {
            
        }

        public void InitializeTables()
        {
            using (var conn = _connectionFactory.GetConnection())
            {
                try
                {
                    conn.CreateTable<JournalRow>();
                }
                catch (Exception e)
                {
                    _logger.Warning(e,$"Could not Create Journal Table {_journalConfig.TableConfig.TableName} as requested by config.");
                }

                if (_journalConfig.DaoConfig.SqlCommonCompatibilityMode)
                {
                    try
                    {
                        conn.CreateTable<JournalMetaData>();
                    }
                    catch (Exception e)
                    {
                        _logger.Warning(e,$"Could not Create Journal Metadata Table {_journalConfig.TableConfig.TableName} as requested by config.");
                    }
                }
            }
        }
    }
}