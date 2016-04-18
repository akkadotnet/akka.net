using System;
using Akka.Configuration;
using Akka.TestKit.Xunit2;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.Tests
{
    public class PersistenceQuerySpec : TestKit.Xunit2.TestKit
    {
        public static readonly Config Config = DummyReadJournalProvider.Config.WithFallback(ConfigurationFactory.Default());

        public PersistenceQuerySpec(ITestOutputHelper output) : base(Config, output: output)
        {
        }

        [Fact]
        public void ReadJournal_should_be_found_by_full_config_key()
        {
            PersistenceQuery.Get(Sys).ReadJournalFor<DummyReadJournal>(DummyReadJournal.Identifier);
        }

        [Fact]
        public void ReadJournal_should_throw_if_unable_to_find_query_journal_by_config_key()
        {
            Assert.Throws<ArgumentException>(() => 
                PersistenceQuery.Get(Sys).ReadJournalFor<DummyReadJournal>(DummyReadJournal.Identifier + "-fail"));
        }
    }
}