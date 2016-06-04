
using System;
using Akka.Configuration;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.Sql.Tests
{
    public class DefaultConfigSpec : TestKit.Xunit2.TestKit
    {
        public DefaultConfigSpec(ITestOutputHelper output) : base(Config.Empty, output: output)
        {
        }

        [Fact]
        public void SqlReadJournal_applies_default_config()
        {
            var readJournal = Sys.ReadJournalFor<SqlReadJournal>(SqlReadJournal.Identifier);
            var config = Sys.Settings.Config.GetConfig("akka.persistence.query.journal.sql");

            config.Should().NotBeNull();
            config.GetString("class").Should().Be("Akka.Persistence.Query.Sql.SqlReadJournalProvider, Akka.Persistence.Query.Sql");
            config.GetTimeSpan("refresh-interval").Should().Be(TimeSpan.FromSeconds(3));
            config.GetInt("max-buffer-size").Should().Be(100);
        }
    }
}