﻿//-----------------------------------------------------------------------
// <copyright file="TestJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Event;
using Akka.Actor;
using Akka.Persistence.Journal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Akka.Persistence.TestKit
{
    /// <summary>
    ///     In-memory persistence journal implementation which behavior could be controlled by interceptors.
    /// </summary>
    public sealed class TestJournal : MemoryJournal
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private IJournalInterceptor _writeInterceptor = JournalInterceptors.Noop.Instance;
        private IJournalInterceptor _recoveryInterceptor = JournalInterceptors.Noop.Instance;

        public TestJournal(Config journalConfig)
        {
            DebugEnabled = journalConfig.GetBoolean("debug", false);
        }

        private bool DebugEnabled { get; }

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case UseWriteInterceptor use:
                    if(DebugEnabled)
                        _log.Info("Using write interceptor {0}", use.Interceptor.GetType().Name);
                    _writeInterceptor = use.Interceptor;
                    Sender.Tell(Ack.Instance);
                    return true;

                case UseRecoveryInterceptor use:
                    if(DebugEnabled)
                        _log.Info("Using recovery interceptor {0}", use.Interceptor.GetType().Name);
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
                try
                {
                    foreach (var p in (IEnumerable<IPersistentRepresentation>)w.Payload)
                    {
                        if(DebugEnabled)
                           _log.Info("Beginning write intercept of message {0} with interceptor {1}", p, _writeInterceptor.GetType().Name);
                        await _writeInterceptor.InterceptAsync(p);
                        
                        if(DebugEnabled)
                            _log.Info("Completed write intercept of message {0} with interceptor {1}", p, _writeInterceptor.GetType().Name);
                        Add(p);
                    }
                }
                catch (TestJournalRejectionException rejected)
                {
                    // i.e. problems with data: corrupted data-set, problems in serialization, constraints, etc.
                    exceptions.Add(rejected);
                    continue;
                }
                catch (TestJournalFailureException)
                {
                    // i.e. data-store problems: network, invalid credentials, etc.
                    throw;
                }
                exceptions.Add(null);
            }

            return exceptions.ToImmutableList();
        }

        public override async Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            var highest = HighestSequenceNr(persistenceId);
            
            if(DebugEnabled)
                _log.Info("Replaying messages from {0} to {1} for persistenceId {2}", fromSequenceNr, toSequenceNr, persistenceId);
            
            if (highest != 0L && max != 0L)
            {
                var messages = Read(persistenceId, fromSequenceNr, Math.Min(toSequenceNr, highest), max);
                foreach (var p in messages)
                {
                    try
                    {
                        if(DebugEnabled)
                            _log.Info("Beginning recovery intercept of message {0} with interceptor {1}", p, _recoveryInterceptor.GetType().Name);
                        await _recoveryInterceptor.InterceptAsync(p);
                        
                        if(DebugEnabled)
                            _log.Info("Completed recovery intercept of message {0} with interceptor {1}", p, _recoveryInterceptor.GetType().Name);
                        recoveryCallback(p);
                    }
                    catch (TestJournalFailureException)
                    {
                        // i.e. problems with data: corrupted data-set, problems in serialization
                        // i.e. data-store problems: network, invalid credentials, etc.
                        throw;
                    }
                }
                
                if(DebugEnabled)
                    _log.Info("Completed replaying messages from {0} to {1} for persistenceId {2}", fromSequenceNr, toSequenceNr, persistenceId);
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
            public static readonly Ack Instance = new();
        }

        internal class TestJournalWrapper : ITestJournal
        {
            public TestJournalWrapper(IActorRef actor)
            {
                _actor = actor;
            }

            private readonly IActorRef _actor;

            public JournalWriteBehavior OnWrite => new(new JournalWriteBehaviorSetter(_actor));

            public JournalRecoveryBehavior OnRecovery => new(new JournalRecoveryBehaviorSetter(_actor));
        }
    }
}
