using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Utils;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery
{
    internal class FlushOnShutdown : UntypedActor
    {
        public static Props Props(
            TaskCompletionSource<Done> done,
            TimeSpan timeout,
            IInboundContext inboundContext,
            ImmutableHashSet<Association> associations)
        {
            associations.Requiring(a => !a.IsEmpty, $"{nameof(associations)} must not be empty");
            return new Props(typeof(FlushOnShutdown), new object[] { done, timeout, inboundContext, associations });
        }

        private class TimeoutMessage
        {
            public static TimeoutMessage Instance = new TimeoutMessage();
            private TimeoutMessage() { }
        }

        public TaskCompletionSource<Done> Done { get; }
        public TimeSpan Timeout { get; }
        public IInboundContext InboundContext { get; }
        public ImmutableHashSet<Association> Associations { get; }

        private ImmutableDictionary<UniqueAddress, int> _remaining = ImmutableDictionary<UniqueAddress, int>.Empty;

        private readonly ICancelable _timeoutTask;
        private readonly ILoggingAdapter _log;

        public FlushOnShutdown(
            TaskCompletionSource<Done> done,
            TimeSpan timeout,
            IInboundContext inboundContext,
            ImmutableHashSet<Association> associations)
        {
            Done = done;
            Timeout = timeout;
            InboundContext = inboundContext;
            Associations = associations;

            // ARTERY: Scala code can explicitly state which threading context a scheduled lambda is being run, WE DON'T HAVE THIS
            // val timeoutTask = context.system.scheduler.scheduleOnce(timeout, self, FlushOnShutdown.Timeout)(context.dispatcher)
            _timeoutTask = Context.System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, TimeoutMessage.Instance, Self);

            _log = Context.GetLogger();
        }

        protected override void PreStart()
        {
            try
            {
                foreach (var a in Associations)
                {
                    var ackExpected = a.SendTerminationHint(Self);
                    if (a.AssociationState.UniqueRemoteAddress.HasValue)
                    {
                        var address = a.AssociationState.UniqueRemoteAddress.Value;
                        _remaining = _remaining.Add(address, ackExpected);
                    }
                    // else Ignore, handshake was not completed on this association
                }

                if (_remaining.Values.Sum() == 0)
                {
                    Done.TrySetResult(Akka.Done.Instance);
                    Context.Stop(Self);
                }
            }
            catch (Exception e)
            {
                if (!e.NonFatal()) throw;

                Done.TrySetException(e);
                throw;
            }
        }

        protected override void PostStop()
        {
            _timeoutTask.Cancel();
            Done.TrySetResult(Akka.Done.Instance);
            _log.Debug("FlushOnShutdown stopped");
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case ActorSystemTerminatingAck from:
                    _log.Debug("ActorSystemTerminatingAck from [{0}]", from);
                    // Just treat unexpected acks as systems from which zero acks are expected
                    var acksRemaining = _remaining.GetOrElse(from.From, 0);
                    _remaining = acksRemaining <= 1 ?
                        _remaining.Remove(from.From) :
                        _remaining.SetItem(from.From, acksRemaining - 1);

                    if (_remaining.IsEmpty)
                    {
                        _log.Debug("Flushing completed");
                        Context.Stop(Self);
                    }
                    break;

                case TimeoutMessage _:
                    _log.Debug(
                        "Flush of remote transport timed out after [{0} ms]. Remaining [{1}] associations.",
                        Timeout.TotalMilliseconds.ToString("0.#"),
                        _remaining.Count);
                    Context.Stop(Self);
                    break;
            }
        }
    }
}
