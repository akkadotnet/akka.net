//-----------------------------------------------------------------------
// <copyright file="ConnectionAndProviderSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Query;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Lifecycle
{
    /// <summary>The plugin's long-lived connections do not pool, unless the connection string says so.</summary>
    public class PoolingSpec : EmbeddedSpec
    {
        public PoolingSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private PoolingSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.TT, "akka.loglevel = DEBUG"), nameof(PoolingSpec), output)
        {
        }

        private ILoggingAdapter SpecLog => Logging.GetLogger(Sys, "pooling-spec");

        private static bool? PoolingOf(string connectionString)
            => new SqliteConnectionStringBuilder(connectionString).Pooling;

        [Fact(DisplayName = "Should_turn_pooling_off_When_the_connection_string_does_not_mention_it")]
        public void Should_turn_pooling_off_When_the_connection_string_does_not_mention_it()
        {
            var prepared = ConnectionHolder.Prepare("Data Source=/tmp/pooling-absent.db", SpecLog);

            PoolingOf(prepared).Should().BeFalse();
            new SqliteConnectionStringBuilder(prepared).DataSource.Should().Be("/tmp/pooling-absent.db");
        }

        [Theory(DisplayName = "Should_keep_the_users_value_When_the_connection_string_sets_pooling")]
        [InlineData("Data Source=/tmp/pooling-true.db;Pooling=True", true)]
        [InlineData("Data Source=/tmp/pooling-false.db;Pooling=False", false)]
        public void Should_keep_the_users_value_When_the_connection_string_sets_pooling(string connectionString, bool expected)
        {
            var prepared = ConnectionHolder.Prepare(connectionString, SpecLog);

            prepared.Should().Be(connectionString);
            PoolingOf(prepared).Should().Be(expected);
        }

        [Fact(DisplayName = "Should_log_once_per_connection_string_When_pooling_is_forced_off")]
        public async Task Should_log_once_per_connection_string_When_pooling_is_forced_off()
        {
            var probe = CreateTestProbe();
            Sys.EventStream.Subscribe(probe.Ref, typeof(Debug));
            const string connectionString = "Data Source=/tmp/pooling-logged-once.db";

            ConnectionHolder.Prepare(connectionString, SpecLog);
            ConnectionHolder.Prepare(connectionString, SpecLog);
            ConnectionHolder.Prepare("Data Source=/tmp/pooling-logged-once.db;Pooling=True", SpecLog);

            var message = await probe.FishForMessageAsync<Debug>(
                d => d.Message.ToString()!.Contains("Pooling=False"), Timeout);
            message.Message.ToString().Should().Contain("/tmp/pooling-logged-once.db");
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));
        }

        [Fact(DisplayName = "Should_hand_the_journal_writer_the_users_pooling_value_When_the_string_sets_it")]
        public async Task Should_hand_the_journal_writer_the_users_pooling_value_When_the_string_sets_it()
        {
            using var db = new SqliteTestDb();
            var config = SqliteSpecConfig.Create(db, SqliteTestMode.TT).WithFallback(ConfigurationFactory.ParseString(TestSerializerConfig.Hocon));
            // the db's own connection string, with Pooling added
            var pooled = ConfigurationFactory.ParseString($"akka.persistence.journal.embedded.connection-string = \"{db.HoconConnectionString};Pooling=True\"")
                .WithFallback(config);
            Akka.Persistence.Embedded.Journal.JournalWriter? writer = null;
            Akka.Persistence.Embedded.Journal.JournalWriter.CreatedForTests = created =>
            {
                if (created.ConnectionString.StartsWith(db.ConnectionString, StringComparison.Ordinal))
                    writer = created;
            };
            var system = (ExtendedActorSystem)ActorSystem.Create("pooling-user-value", pooled);
            try
            {
                await Persistence.Instance.Apply(system).JournalFor(null).Ask<Initialized>(EnsureInitialized.Instance, Timeout);
            }
            finally
            {
                Akka.Persistence.Embedded.Journal.JournalWriter.CreatedForTests = null;
                await system.Terminate();
            }

            writer.Should().NotBeNull();
            PoolingOf(writer!.HolderConnectionStringForTests).Should().BeTrue("the user wrote Pooling=True");
        }
    }

    /// <summary>A provider created from HOCON finds its plugin path or refuses to start.</summary>
    public class ProviderPathSpec : EmbeddedSpec
    {
        public ProviderPathSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private ProviderPathSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, SqliteTestMode.TT, """
                    akka.persistence.query.journal.twin-a {
                        class = "Akka.Persistence.Embedded.Query.SqliteReadJournalProvider, Akka.Persistence.Embedded"
                        write-plugin = "akka.persistence.journal.embedded"
                    }
                    akka.persistence.query.journal.twin-b {
                        class = "Akka.Persistence.Embedded.Query.SqliteReadJournalProvider, Akka.Persistence.Embedded"
                        write-plugin = "akka.persistence.journal.embedded"
                    }
                    akka.persistence.query.journal.unique {
                        class = "Akka.Persistence.Embedded.Query.SqliteReadJournalProvider, Akka.Persistence.Embedded"
                        write-plugin = "akka.persistence.journal.embedded"
                        max-buffer-size = 123
                    }
                    """),
                nameof(ProviderPathSpec),
                output)
        {
        }

        private ExtendedActorSystem System => (ExtendedActorSystem)Sys;

        [Fact(DisplayName = "Should_find_the_plugin_path_When_exactly_one_section_matches")]
        public void Should_find_the_plugin_path_When_exactly_one_section_matches()
        {
            var config = Sys.Settings.Config.GetConfig("akka.persistence.query.journal.unique");

            var journal = (SqliteReadJournal)new SqliteReadJournalProvider(System, config).GetReadJournal();

            journal.PluginPathForTests.Should().Be("akka.persistence.query.journal.unique");
        }

        [Fact(DisplayName = "Should_throw_When_no_section_matches_the_config")]
        public void Should_throw_When_no_section_matches_the_config()
        {
            var stranger = ConfigurationFactory.ParseString("write-plugin = \"akka.persistence.journal.embedded\"\nmax-buffer-size = 7");

            var thrown = Assert.Throws<ConfigurationException>(() => new SqliteReadJournalProvider(System, stranger));

            thrown.Message.Should().Contain("0 sections match").And.Contain("WithEmbeddedPersistence");
        }

        [Fact(DisplayName = "Should_throw_When_two_sections_have_the_same_text")]
        public void Should_throw_When_two_sections_have_the_same_text()
        {
            var config = Sys.Settings.Config.GetConfig("akka.persistence.query.journal.twin-a");

            var thrown = Assert.Throws<ConfigurationException>(() => new SqliteReadJournalProvider(System, config));

            thrown.Message.Should().Contain("2 sections match");
        }

        [Fact(DisplayName = "Should_use_the_given_path_When_the_provider_is_created_with_one")]
        public void Should_use_the_given_path_When_the_provider_is_created_with_one()
        {
            var config = Sys.Settings.Config.GetConfig("akka.persistence.query.journal.twin-a");

            var journal = (SqliteReadJournal)new SqliteReadJournalProvider(System, config, "akka.persistence.query.journal.twin-a").GetReadJournal();

            journal.PluginPathForTests.Should().Be("akka.persistence.query.journal.twin-a");
        }
    }
}
