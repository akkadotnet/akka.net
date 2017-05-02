using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Persistence.Internal;

namespace Akka.Persistence
{
    public class RequestRecoveryPermit
    { }

    public class RecoveryPermitGranted
    { }

    public class ReturnRecoveryPermit
    { }

    /// <summary>
    /// When starting many persistent actors at the same time the journal its data store is protected 
    /// from being overloaded by limiting number of recoveries that can be in progress at the same time.
    /// </summary>
    public class RecoveryPermitter : UntypedActor
    {
        private readonly int maxPermits;
        private readonly LinkedList<IActorRef> pending = new LinkedList<IActorRef>();
        private readonly ILoggingAdapter Log = Context.GetLogger();
        private int usedPermits;
        private int maxPendingStats;

        public static Props Props(int maxPermits) =>
            Actor.Props.Create(() => new RecoveryPermitter(maxPermits));

        public RecoveryPermitter(int maxPermits)
        {
            this.maxPermits = maxPermits;
        }

        protected override void OnReceive(object message)
        {
            message.Match()
                .With<RequestRecoveryPermit>(_ =>
                {
                    Context.Watch(Sender);
                    if (usedPermits > maxPermits)
                    {
                        if (pending.Count == 0)
                            Log.Debug("Exceeded max-concurrent-recoveries [{0}]. First pending {1}", maxPermits, Sender);
                        pending.AddLast(Sender);
                        maxPendingStats = Math.Max(maxPendingStats, pending.Count);
                    }
                    else
                    {
                        RecoveryPermitGranted(Sender);
                    }
                })
                .With<ReturnRecoveryPermit>(_ => ReturnRecoveryPermit(Sender))
                .With<Terminated>(terminated =>
                {
                    if (!pending.Remove(terminated.ActorRef))
                        ReturnRecoveryPermit(terminated.ActorRef);
                });
        }

        private void ReturnRecoveryPermit(IActorRef actorRef)
        {
            usedPermits--;
            Context.Unwatch(actorRef);

            if (usedPermits < 0)
                throw new IllegalStateException("Permits must not be negative");

            if (pending.Count > 0)
            {
                var popRef = pending.Pop();
                RecoveryPermitGranted(popRef);
            }

            if (pending.Count != 0 || maxPendingStats <= 0)
                return;

            Log.Debug("Drained pending recovery permit requests, max in progress was [{0}], still [{1}] in progress", usedPermits + maxPendingStats, usedPermits);
            maxPendingStats = 0;
        }

        private void RecoveryPermitGranted(IActorRef actorRef)
        {
            usedPermits++;
            actorRef.Tell(new RecoveryPermitGranted());
        }
    }
}