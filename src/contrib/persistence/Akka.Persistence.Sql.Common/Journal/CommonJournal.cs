//-----------------------------------------------------------------------
// <copyright file="CommonJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Common;
using Akka.Actor;

namespace Akka.Persistence.Sql.Common.Journal
{
    public class CommonJournalEngine : JournalDbEngine
    {
        private readonly DbProviderFactory _dbFactory;

        public CommonJournalEngine(ActorSystem system)
            : base(system)
        {
            var extension = CommonPersistence.Get(system);

            _dbFactory = DbProviderFactories.GetFactory(extension.JournalSettings.ProviderName);
            QueryBuilder = new CommonQueryBuilder(_dbFactory, Settings.TableName);
        }

        protected override string JournalConfigPath { get { return CommonJournalSettings.ConfigPath; } }

        protected override DbConnection CreateDbConnection(string connectionString)
        {
            var connection = _dbFactory.CreateConnection();
            connection.ConnectionString = connectionString;
            return connection;
        }

        protected override void CopyParamsToCommand(DbCommand sqlCommand, JournalEntry entry)
        {
            sqlCommand.Parameters["@PersistenceId"].Value = entry.PersistenceId;
            sqlCommand.Parameters["@SequenceNr"].Value = entry.SequenceNr;
            sqlCommand.Parameters["@IsDeleted"].Value = entry.IsDeleted;
            sqlCommand.Parameters["@Manifest"].Value = entry.Manifest;
            sqlCommand.Parameters["@Timestamp"].Value = entry.Timestamp;
            sqlCommand.Parameters["@Payload"].Value = entry.Payload;
        }
    }

    public class CommonJournal : SqlJournal
    {
        public readonly CommonPersistence Extension = CommonPersistence.Get(Context.System);
        public CommonJournal()
            : base(new CommonJournalEngine(Context.System))
        {
        }
    }
}