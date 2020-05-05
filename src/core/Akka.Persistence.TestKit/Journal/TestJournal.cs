//-----------------------------------------------------------------------
// <copyright file="TestJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

    /// <summary>
    ///     In-memory persistence journal implementation which behavior could be controlled by interceptors.
    /// </summary>
    public sealed class TestJournal : MemoryJournal
    {
        private IJournalInterceptor _writeInterceptor = JournalInterceptors.Noop.Instance;
        private IJournalInterceptor _recoveryInterceptor = JournalInterceptors.Noop.Instance;

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case UseWriteInterceptor use:
                    _writeInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                case UseRecoveryInterceptor use:
                    _recoveryInterceptor = use.Interceptor;
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
                        // i.e. problems with data: corrupted data-set, problems in serialization, constraints, etc.
                        exceptions.Add(rejected);
                    }
                    catch (TestJournalFailureException)
                    {
                        // i.e. data-store problems: network, invalid credentials, etc.
                        throw;
                    }
                }
            }

            return exceptions.ToImmutableList();
        }

        public override async Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            var highest = HighestSequenceNr(persistenceId);
            if (highest != 0L && max != 0L)
            {
                var messages = Read(persistenceId, fromSequenceNr, Math.Min(toSequenceNr, highest), max);
                foreach (var p in messages)
                {
                    try
                    {
                        await _recoveryInterceptor.InterceptAsync(p);
                        recoveryCallback(p);
                    }
                    catch (TestJournalFailureException)
                    {
                        // i.e. problems with data: corrupted data-set, problems in serialization
                        // i.e. data-store problems: network, invalid credentials, etc.
                        throw;
                    }
                }
            }
        }

        /// <summary>
        ///     Create proxy object from journal actor reference which can alter behavior of journal.
        /// </summary>
        /// <remarks>
        ///     Journal actor must be of <see cref="TestJournal"/> type.
        /// </remarks>
        /// <param name="actor">Journal actor reference.</param>
        /// <returns>Proxy object to control <see cref="TestJournal"/>.</returns>
        public static ITestJournal FromRef(IActorRef actor)
        {
            return new TestJournalWrapper(actor);
        }

        public sealed class UseWriteInterceptor
        {
            public UseWriteInterceptor(IJournalInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public IJournalInterceptor Interceptor { get; }
        }

        public sealed class UseRecoveryInterceptor
        {
            public UseRecoveryInterceptor(IJournalInterceptor interceptor)
            {
                Interceptor = interceptor;
            }

            public IJournalInterceptor Interceptor { get; }
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

            public JournalWriteBehavior OnWrite => new JournalWriteBehavior(new JournalWriteBehaviorSetter(_actor));

            public JournalRecoveryBehavior OnRecovery => new JournalRecoveryBehavior(new JournalRecoveryBehaviorSetter(_actor));
        }
    }
}
