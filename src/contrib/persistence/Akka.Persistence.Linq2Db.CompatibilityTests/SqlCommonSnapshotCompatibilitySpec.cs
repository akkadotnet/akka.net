using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2.Internals;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public abstract class SqlCommonSnapshotCompatibilitySpec
    {
        protected abstract Configuration.Config Config { get; }
        public SqlCommonSnapshotCompatibilitySpec(
            ITestOutputHelper outputHelper)
        {
            Output = outputHelper;
        }

        protected void InitializeLogger(ActorSystem system)
        {
            if (Output != null)
            {
                var extSystem = (ExtendedActorSystem)system;
                var logger = extSystem.SystemActorOf(Props.Create(() => new TestOutputLogger(Output)), "log-test");
                logger.Tell(new InitializeLogger(system.EventStream));
            }
        }
        public ITestOutputHelper Output { get; set; }
        protected abstract string OldSnapshot { get; }
        protected abstract string NewSnapshot { get; }
        
        [Fact]
        public async Task Can_Recover_SqlCommon_Snapshot()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(OldSnapshot,
                    "p-1")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef =  sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(NewSnapshot,
                    "p-1")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(5)).Result);
        }
        [Fact]
        public async Task Can_Persist_SqlCommon_Snapshot()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(OldSnapshot,
                    "p-2")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef =  sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(NewSnapshot,
                    "p-2")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(10)).Result);
            var ourSecondGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourSecondGuid, Number = 2});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourSecondGuid},TimeSpan.FromSeconds(5)).Result);
        }
        
        [Fact]
        public async Task SqlCommon_Snapshot_Can_Recover_L2Db_Snapshot()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(NewSnapshot,
                    "p-3")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef = sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(OldSnapshot,
                    "p-3")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(5)).Result);
        }
        [Fact]
        public async Task SqlCommon_Snapshot_Can_Persist_L2db_Snapshot()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(NewSnapshot,
                    "p-4")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await Task.Delay(TimeSpan.FromSeconds(2));
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef =  sys1.ActorOf(Props.Create(() =>
                new SnapshotCompatActor(OldSnapshot,
                    "p-4")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(10)).Result);
            var ourSecondGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourSecondGuid, Number = 2});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourSecondGuid},TimeSpan.FromSeconds(5)).Result);
        }
    }
}