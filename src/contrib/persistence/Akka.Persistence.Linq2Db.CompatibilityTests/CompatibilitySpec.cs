using System;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2.Internals;
using JetBrains.dotMemoryUnit;
using JetBrains.dotMemoryUnit.Kernel;
using Xunit;
using Xunit.Abstractions;
using Config = Docker.DotNet.Models.Config;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    public abstract class CompatibilitySpec
    {
        

        public CompatibilitySpec(ITestOutputHelper outputHelper)
        {
            Output = outputHelper;
        }

        public ITestOutputHelper Output { get; set; }

        protected void InitializeLogger(ActorSystem system)
        {
            if (Output != null)
            {
                var extSystem = (ExtendedActorSystem)system;
                var logger = extSystem.SystemActorOf(Props.Create(() => new TestOutputLogger(Output)), "log-test");
                logger.Tell(new InitializeLogger(system.EventStream));
            }
        }

        protected abstract Configuration.Config Config { get; }

        protected abstract string OldJournal { get; }
        protected abstract string NewJournal { get; }
        [Fact]
        public async Task Can_Recover_SqlCommon_Journal()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new PersistActor(OldJournal,
                    "p-1")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef =  sys1.ActorOf(Props.Create(() =>
                new PersistActor(NewJournal,
                    "p-1")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(5)).Result);
        }
        [Fact]
        public async Task Can_Persist_SqlCommon_Journal()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new PersistActor(OldJournal,
                    "p-2")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef =  sys1.ActorOf(Props.Create(() =>
                new PersistActor(NewJournal,
                    "p-2")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(10)).Result);
            var ourSecondGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourSecondGuid, Number = 2});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourSecondGuid},TimeSpan.FromSeconds(5)).Result);
        }
        
        [Fact]
        public async Task SqlCommon_Journal_Can_Recover_L2Db_Journal()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new PersistActor(NewJournal,
                    "p-3")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef = sys1.ActorOf(Props.Create(() =>
                new PersistActor(OldJournal,
                    "p-3")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(5)).Result);
        }
        [Fact]
        public async Task SqlCommon_Journal_Can_Persist_L2db_Journal()
        {
            var sys1 = ActorSystem.Create("first",
                Config);
            InitializeLogger(sys1);
            var persistRef = sys1.ActorOf(Props.Create(() =>
                new PersistActor(NewJournal,
                    "p-4")), "test");
            var ourGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourGuid, Number = 1});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid}, TimeSpan.FromSeconds(5)).Result);
            await persistRef.GracefulStop(TimeSpan.FromSeconds(5));
            persistRef =  sys1.ActorOf(Props.Create(() =>
                new PersistActor(OldJournal,
                    "p-4")), "test");
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourGuid},TimeSpan.FromSeconds(10)).Result);
            var ourSecondGuid = Guid.NewGuid();
            persistRef.Tell(new SomeEvent(){EventName = "rec-test", Guid = ourSecondGuid, Number = 2});
            Assert.True(persistRef.Ask<bool>(new ContainsEvent(){Guid = ourSecondGuid},TimeSpan.FromSeconds(5)).Result);
        }
    }
}