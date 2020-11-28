using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.TCK.Journal;
using LinqToDB;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public class SQLServerJournalCustomConfigSpec : JournalSpec
    {

        private static readonly Configuration.Config conf =
            Linq2DbJournalDefaultSpecConfig.GetCustomConfig("customspec",
                "customjournalSpec", "customjournalmetadata",
                ProviderName.SqlServer2017, ConnectionString.Instance, true);
        public SQLServerJournalCustomConfigSpec(ITestOutputHelper outputHelper)
            : base(conf, "SQLServer-custom", outputHelper)
        {
            var connFactory = new AkkaPersistenceDataConnectionFactory(new JournalConfig(conf.GetConfig("akka.persistence.journal.linq2db.customspec")));
            using (var conn = connFactory.GetConnection())
            {
                try
                {
                    conn.GetTable<JournalRow>().Delete();
                }
                catch (Exception e)
                {
                   
                }
                try
                {
                    conn.GetTable<JournalMetaData>().Delete();
                }
                catch (Exception e)
                {
                   
                }
            }

            Initialize();
        }
        // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
        protected override bool SupportsSerialization => false;
    }
}