using Akka.Configuration;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Query.Sqlite.TestKit;

public class DefaultConfigSpec : Akka.TestKit.Xunit2.TestKit
{
    public DefaultConfigSpec(ITestOutputHelper output) : base(Config.Empty, output: output)
    {
    }

    [Fact]
    public void SqliteReadJournal_applies_default_config()
    {
        _ = Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
        var config = Sys.Settings.Config.GetConfig("akka.persistence.query.journal.sqlite");
        
        config.IsNullOrEmpty().Should().BeFalse();
        config.GetString("class", null).Should().Be("Akka.Persistence.Query.Sqlite.SqliteReadJournalProvider, Akka.Persistence.Query.Sqlite");
        config.GetTimeSpan("refresh-interval", TimeSpan.MinValue).Should().Be(TimeSpan.FromSeconds(3));
        config.GetInt("max-buffer-size", 0).Should().Be(100);
    }
}