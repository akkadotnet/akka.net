//-----------------------------------------------------------------------
// <copyright file="PersistenceTestKitBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.TestKit;
    using Configuration;

    public abstract class PersistenceTestKitBase : TestKitBase
    {
        protected PersistenceTestKitBase(ITestKitAssertions assertions, string actorSystemName = null, string testActorName = null)
            : base(assertions, GetConfig(), actorSystemName, testActorName)
        {
            JournalActorRef = GetJournalRef(Sys);
            Journal = TestJournal.FromRef(JournalActorRef);
        }

        public IActorRef JournalActorRef { get; }

        public ITestJournal Journal { get; } 

        public async Task WithJournalRecovery(Func<JournalRecoveryBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Journal.OnRecovery);
                await execution();
            }
            finally
            {
                await Journal.OnRecovery.Pass();
            }
        }

        public async Task WithJournalWrite(Func<JournalWriteBehavior, Task> behaviorSelector, Func<Task> execution)
        {
            try
            {
                await behaviorSelector(Journal.OnWrite);
                await execution();
            }
            finally
            {
                await Journal.OnWrite.Pass();
            }
        }

        public Task WithJournalRecovery(Func<JournalRecoveryBehavior, Task> behaviorSelector, Action execution)
            => WithJournalRecovery(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(new object());
            });

        public Task WithJournalWrite(Func<JournalWriteBehavior, Task> behaviorSelector, Action execution)
            => WithJournalWrite(behaviorSelector, () =>
            {
                execution();
                return Task.FromResult(new object());
            });

        public void WithFailingJournalRecovery(Action execution)
        {
            try
            {
                Journal.OnRecovery.Fail();
                execution();
            }
            finally
            {
                // restore normal functionality
                Journal.OnRecovery.Pass();
            }
        }

        public void WithFailingJournalWrites(Action execution)
        {
            try
            {
                Journal.OnWrite.Fail();
                execution();
            }
            finally
            {
                // restore normal functionality
                Journal.OnWrite.Pass();
            }
        }       

        static IActorRef GetJournalRef(ActorSystem sys)
            => Persistence.Instance.Apply(sys).JournalFor(null);

        static Config GetConfig()
            => ConfigurationFactory.FromResource<PersistenceTestKit>("Akka.Persistence.TestKit.test-journal.conf");
    }
}