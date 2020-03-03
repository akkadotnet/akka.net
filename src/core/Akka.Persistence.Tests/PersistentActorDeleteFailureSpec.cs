//-----------------------------------------------------------------------
// <copyright file="PersistentActorDeleteFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class PersistentActorDeleteFailureSpec : PersistenceSpec
    {
        internal class DeleteTo
        {
            public long N { get; private set; }

            public DeleteTo(long n)
            {
                N = n;
            }
        }

        [Serializable]
        public class SimulatedException : Exception
        {
            public SimulatedException()
            {
            }

            public SimulatedException(string message) : base(message)
            {
            }
        }

        public class DeleteFailingMemoryJournal : MemoryJournal
        {
            protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
            {
                var promise = new TaskCompletionSource<object>();
                promise.SetException(new SimulatedException("Boom! Unable to delete events!"));
                return promise.Task;
            }
        }

        public class DoesNotHandleDeleteFailureActor : PersistentActor
        {
            private readonly string _name;
            public DoesNotHandleDeleteFailureActor(string name)
            {
                _name = name;
            }

            public override string PersistenceId { get { return _name; } }

            protected override bool ReceiveRecover(object message)
            {
                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is DeleteTo)
                {
                    DeleteMessages(((DeleteTo)message).N);
                    return true;
                }
                return false;
            }
        }

        public class HandlesDeleteFailureActor : PersistentActor
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            public HandlesDeleteFailureActor(string name, IActorRef probe)
            {
                _name = name;
                _probe = probe;
            }

            public override string PersistenceId { get { return _name; } }

            protected override bool ReceiveRecover(object message)
            {
                return false;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is DeleteTo)
                    DeleteMessages(((DeleteTo)message).N);
                if (message is DeleteMessagesFailure)
                    _probe.Tell(message);
                else return false;
                return true;
            }
        }

        public PersistentActorDeleteFailureSpec() : base(Configuration("PersistentActorDeleteFailureSpec",
            serialization: "off",
            extraConfig: "akka.persistence.journal.inmem.class = \"Akka.Persistence.Tests.PersistentActorDeleteFailureSpec+DeleteFailingMemoryJournal, Akka.Persistence.Tests\""))
        {
        }

        [Fact]
        public void PersistentActor_should_have_default_warn_logging_be_triggered_when_deletion_failed()
        {
            var pref = Sys.ActorOf(Props.Create(() => new DoesNotHandleDeleteFailureActor(Name)));
            Sys.EventStream.Subscribe(TestActor, typeof (Warning));
            pref.Tell(new DeleteTo(100));
            var message = ExpectMsg<Warning>().Message.ToString();
            message.Contains("Failed to DeleteMessages").ShouldBeTrue();
            message.Contains("Boom! Unable to delete events!").ShouldBeTrue();
        }

        [Fact]
        public void PersistentActor_should_receive_a_DeleteMessagesFailure_when_deletion_failed_and_the_default_logging_should_not_be_triggered()
        {
            var pref = Sys.ActorOf(Props.Create(() => new HandlesDeleteFailureActor(Name, TestActor)));
            Sys.EventStream.Subscribe(TestActor, typeof (Warning));
            pref.Tell(new DeleteTo(100));
            ExpectMsg<DeleteMessagesFailure>(m => m.ToSequenceNr == 100);
            ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }
    }
}
