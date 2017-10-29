﻿//-----------------------------------------------------------------------
// <copyright file="RecoveryPermitter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Internal;

namespace Akka.Persistence
{
    internal sealed class RequestRecoveryPermit
    {
        public RequestRecoveryPermit(object correlationId)
        {
            CorrelationId = correlationId;
        }

        public object CorrelationId { get; }

    }

    internal sealed class RecoveryPermitGranted
    {
        public RecoveryPermitGranted(object correlationId)
        {
            CorrelationId = correlationId;
        }

        public object CorrelationId { get; }
    }

    internal sealed class ReturnRecoveryPermit
    {
        public static ReturnRecoveryPermit Instance { get; } = new ReturnRecoveryPermit();
        private ReturnRecoveryPermit() { }
    }

    /// <summary>
    /// When starting many persistent actors at the same time the journal its data store is protected 
    /// from being overloaded by limiting number of recoveries that can be in progress at the same time.
    /// </summary>
    internal class RecoveryPermitter : UntypedActor
    {
        private readonly LinkedList<IActorRef> pending = new LinkedList<IActorRef>();
        private readonly ILoggingAdapter Log = Context.GetLogger();
        private int _usedPermits;
        private int _maxPendingStats;

        public static Props Props(int maxPermits) =>
            Actor.Props.Create(() => new RecoveryPermitter(maxPermits));

        public int MaxPermits { get; }

        public RecoveryPermitter(int maxPermits)
        {
            MaxPermits = maxPermits;
        }

        protected override void OnReceive(object message)
        {
            if (message is RequestRecoveryPermit req)
            {
                Context.Watch(Sender);
                if (_usedPermits >= MaxPermits)
                {
                    if (pending.Count == 0)
                        Log.Debug("Exceeded max-concurrent-recoveries [{0}]. First pending {1}", MaxPermits, Sender);
                    pending.AddLast(Sender);
                    _maxPendingStats = Math.Max(_maxPendingStats, pending.Count);
                }
                else
                {
                    FinalizeRecoveryPermitGranted(Sender);
                }
            }
            else if (message is ReturnRecoveryPermit)
            {
                ReturnRecoveryPermit(Sender);
            }
            else if (message is Terminated terminated && !pending.Remove(terminated.ActorRef))
            {
                // pre-mature termination should be rare
                ReturnRecoveryPermit(terminated.ActorRef);
            }
        }

        private void ReturnRecoveryPermit(IActorRef actorRef)
        {
            _usedPermits--;
            Context.Unwatch(actorRef);

            if (_usedPermits < 0)
                throw new IllegalStateException("Permits must not be negative");

            if (pending.Count > 0)
            {
                var popRef = pending.Pop();
                FinalizeRecoveryPermitGranted(popRef);
            }

            if (pending.Count != 0 || _maxPendingStats <= 0)
                return;

            Log.Debug("Drained pending recovery permit requests, max in progress was [{0}], still [{1}] in progress", _usedPermits + _maxPendingStats, _usedPermits);
            _maxPendingStats = 0;
        }

        private void FinalizeRecoveryPermitGranted(IActorRef actorRef)
        {
            _usedPermits++;
            actorRef.Tell(RecoveryPermitGranted.Instance);
        }
    }
}