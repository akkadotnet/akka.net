//-----------------------------------------------------------------------
// <copyright file="LifecycleSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Lifecycle
{
    public class ShutdownSpec
    {
        private static bool HoldsOpenHandleTo(string path)
        {
            if (!OperatingSystem.IsLinux())
                return false;

            foreach (var fd in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
            {
                try
                {
                    var target = new FileInfo(fd).LinkTarget;
                    if (target is not null && target.StartsWith(path, StringComparison.Ordinal))
                        return true;
                }
                catch (IOException)
                {
                    // the descriptor closed while we looked at it
                }
            }

            return false;
        }

        [Fact(DisplayName = "Should_release_database_file_When_actor_system_terminates")]
        public async Task Should_release_database_file_When_actor_system_terminates()
        {
            using var db = new SqliteTestDb();
            var system = (ExtendedActorSystem)ActorSystem.Create("release-file", SqliteSpecConfig.Create(db, SqliteTestMode.TT));
            var persistence = Persistence.Instance.Apply(system);

            // touch every component that opens a connection: writer, readers, snapshot worker, query threads
            await persistence.JournalFor(null).Ask<Initialized>(EnsureInitialized.Instance, TimeSpan.FromSeconds(10));
            await persistence.SnapshotStoreFor(null).Ask<Initialized>(EnsureInitialized.Instance, TimeSpan.FromSeconds(10));
            var readJournal = system.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
            await readJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), system.Materializer()).WaitAsync(TimeSpan.FromSeconds(10));
            if (OperatingSystem.IsLinux())
                HoldsOpenHandleTo(db.FilePath).Should().BeTrue("the plugin keeps connections open while it runs");

            await system.Terminate();

            // the read journal closes its threads from a termination callback; give it a moment
            var released = await WaitUntilAsync(() => !HoldsOpenHandleTo(db.FilePath), TimeSpan.FromSeconds(10));
            released.Should().BeTrue("no thread of the plugin may keep the database file open after shutdown");
            File.Delete(db.FilePath); // must succeed on every platform
            File.Exists(db.FilePath).Should().BeFalse();
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                    return false;
                await Task.Yield();
                await Task.Delay(20);
            }

            return true;
        }
    }

    public class RestartSpec : EmbeddedSpec
    {
        public RestartSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private RestartSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.TT), nameof(RestartSpec), output)
        {
        }

        [Fact(DisplayName = "Should_restart_and_recover_When_journal_actor_crashes")]
        public async Task Should_restart_and_recover_When_journal_actor_crashes()
        {
            (await WriteAsync(Write(Evt("survivor", 1, new TestEvent("a")), Evt("survivor", 2, new TestEvent("b"))))).Succeeded.Should().BeTrue();

            // a message the journal cannot process crashes it; the default supervisor restarts it
            await EventFilter.Exception<ArgumentNullException>().ExpectOneAsync(() =>
            {
                Journal.Tell(new WriteFinished(null!, Task.CompletedTask));
                return Task.CompletedTask;
            });

            await InitializeJournalAsync(); // answered once the restarted journal finished its init

            var replay = await ReplayAsync("survivor");
            replay.Replayed.Select(p => p.SequenceNr).Should().Equal(1L, 2L);
            (await WriteAsync(Write(Evt("survivor", 3, new TestEvent("c"))))).Succeeded.Should().BeTrue();
            (await ReplayAsync("survivor")).HighestSequenceNr.Should().Be(3L);
        }
    }
}
