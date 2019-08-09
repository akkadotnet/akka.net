//-----------------------------------------------------------------------
// <copyright file="TestJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using Akka.Actor;
    using Akka.Persistence;
    using Akka.Persistence.Journal;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Threading.Tasks;

    public interface ITestJournal
    {
        JournalWriteBehavior OnWrite { get; }
    }

    public sealed class TestJournal : MemoryJournal
    {
        private IJournalWriteInterceptor _writeInterceptor = JournalWriteInterceptors.Noop.Instance;

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case UseWriteInterceptor use:
                    _writeInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;
                
                default:
                    return base.ReceivePluginInternal(message);
            }
        }

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var exceptions = new List<Exception>();
            foreach (var w in messages)
            {
                foreach (var p in (IEnumerable<IPersistentRepresentation>) w.Payload)
                {
                    try
                    {
                        await _writeInterceptor.InterceptAsync(p);
                        Add(p);
                        exceptions.Add(null);
                    }
                    catch (TestJournalRejectionException rejected)
                    {
                        exceptions.Add(rejected);
                    }
                    catch (TestJournalFailureException)
                    {
                        throw;
                    }
                }
            }

            return exceptions.ToImmutableList();
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            return base.ReplayMessagesAsync(context, persistenceId, fromSequenceNr, toSequenceNr, max, recoveryCallback);
        }

        public static ITestJournal FromRef(IActorRef actor)
        {
            return  new TestJournalWrapper(actor);
        }

        public sealed class UseWriteInterceptor
        {
            public UseWriteInterceptor(IJournalWriteInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public IJournalWriteInterceptor Interceptor { get; }
        }

        public sealed class Ack
        {
            public static readonly Ack Instance = new Ack();
        }

        internal class TestJournalWrapper : ITestJournal
        {
            public TestJournalWrapper(IActorRef actor)
            {
                _actor = actor;
            }

            private readonly IActorRef _actor;

            public JournalWriteBehavior OnWrite => new JournalWriteBehavior(_actor);
        }
    }
}
