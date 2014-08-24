using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    public class MaxUnconfirmedMessagesExceededException : AkkaException
    {
        public MaxUnconfirmedMessagesExceededException(string message, Exception cause = null)
            : base(message, cause)
        {
        }
    }

    public struct GuaranteedDeliverySnapshot : IMessage
    {
        public GuaranteedDeliverySnapshot(long deliveryId, IEnumerable<UnconfirmedDelivery> unconfirmedDeliveries)
            : this()
        {
            DeliveryId = deliveryId;
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public long DeliveryId { get; private set; }
        public IEnumerable<UnconfirmedDelivery> UnconfirmedDeliveries { get; private set; }
    }

    public struct UnconfirmedWarning
    {
        public UnconfirmedWarning(IEnumerable<UnconfirmedDelivery> unconfirmedDeliveries)
            : this()
        {
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public IEnumerable<UnconfirmedDelivery> UnconfirmedDeliveries { get; set; }
    }

    public struct UnconfirmedDelivery
    {
        public UnconfirmedDelivery(long deliveryId, ActorPath destination, object message)
            : this()
        {
            DeliveryId = deliveryId;
            Destination = destination;
            Message = message;
        }

        public long DeliveryId { get; set; }
        public ActorPath Destination { get; set; }
        public object Message { get; set; }
    }

    internal struct Delivery
    {
        public Delivery(ActorPath destination, object message, DateTime timestamp, int attempt)
            : this()
        {
            Destination = destination;
            Message = message;
            Timestamp = timestamp;
            Attempt = attempt;
        }

        public ActorPath Destination { get; private set; }
        public object Message { get; private set; }
        public DateTime Timestamp { get; private set; }
        public int Attempt { get; private set; }

        public Delivery IncrementedCopy()
        {
            return new Delivery(Destination, Message, Timestamp, Attempt + 1);
        }
    }

    internal sealed class RedeliveryTick
    {
        public static readonly RedeliveryTick Instance = new RedeliveryTick();
        private RedeliveryTick()
        {
        }
    }

    public interface IGuaranteedDelivery
    {
        TimeSpan RedeliverInterval { get; }
        int UnconfirmedAttemptsToWarn { get; }
        int MaxUnconfirmedMessages { get; }
        int UnconfirmedMessages { get; }

        GuaranteedDeliverySnapshot GetDeliverySnapshot();
        void SetDeliverySnapshot(GuaranteedDeliverySnapshot snapshot);

        void Deliver(ActorPath destination, Func<long, object> idToMessageMapper);

        bool ConfirmDelivery(long deliveryId);
    }

    public class GuaranteedDeliverer : IGuaranteedDelivery
    {
        private readonly IActorContext _context;
        private CancellationTokenSource _redeliveryTaskCanceler;
        private Task _redeliverTask;

        private long _deliverySequenceNr = 0L;
        private SortedList<long, Delivery> _unconfirmed = new SortedList<long, Delivery>();

        public GuaranteedDeliverer(IActorContext actorContext)
        {
            _context = actorContext;
            var persistence = actorContext.System.GetExtension<Persistence>();
            if (persistence == null)
            {
                throw new ArgumentException("Provided actor context refers to actor system, which doesn't have necessary persistance extension initialized");
            }

            var persistenceSettings = persistence.Settings;
            RedeliverInterval = persistenceSettings.AtLeastOnceDelivery.RedeliverInterval;
            UnconfirmedAttemptsToWarn = persistenceSettings.AtLeastOnceDelivery.UnconfirmedAttemptsToWarn;
            MaxUnconfirmedMessages = persistenceSettings.AtLeastOnceDelivery.MaxUnconfirmedMessages;

            _redeliverTask = RedeliverTask(out _redeliveryTaskCanceler);
        }

        public TimeSpan RedeliverInterval { get; private set; }
        public int UnconfirmedAttemptsToWarn { get; private set; }
        public int MaxUnconfirmedMessages { get; private set; }
        public int UnconfirmedMessages { get { return _unconfirmed.Count; } }

        public void Deliver(ActorPath destination, Func<long, object> idToMessageMapper)
        {
            if (_unconfirmed.Count >= MaxUnconfirmedMessages)
            {
                throw new MaxUnconfirmedMessagesExceededException("To many unconfirmed messages, maximum allowed is " + MaxUnconfirmedMessages);
            }

            var recoveryRunning = false;    /* ((Processor)this).RecoveryRunning */
            var deliveryId = Interlocked.Increment(ref _deliverySequenceNr);
            var now = recoveryRunning
                ? DateTime.UtcNow - RedeliverInterval
                : DateTime.UtcNow;
            var delivery = new Delivery(destination, idToMessageMapper(deliveryId), now, 0);

            if (recoveryRunning)
            {
                _unconfirmed.Add(deliveryId, delivery);
            }
            else
            {
                Send(deliveryId, delivery, now);
            }
        }

        public bool ConfirmDelivery(long deliveryId)
        {
            if (_unconfirmed.ContainsKey(deliveryId))
            {
                _unconfirmed.Remove(deliveryId);
                return true;
            }
            return false;
        }

        public void CancelRedelivery()
        {
            _redeliveryTaskCanceler.Cancel();
        }

        public void RedeliverOverdue()
        {
            var now = DateTime.UtcNow;
            var deadline = now - RedeliverInterval;
            var warnings = new List<UnconfirmedDelivery>();

            foreach (var entry in _unconfirmed)
            {
                var deliveryId = entry.Key;
                var delivery = entry.Value;

                if (delivery.Timestamp <= deadline)
                {
                    Send(deliveryId, delivery, now);

                    if (delivery.Attempt == UnconfirmedAttemptsToWarn)
                    {
                        warnings.Add(new UnconfirmedDelivery(deliveryId, delivery.Destination, delivery.Message));
                    }
                }
            }

            if (warnings.Count > 0)
            {
                _context.Self.Tell(new UnconfirmedWarning(warnings));
            }
        }

        public GuaranteedDeliverySnapshot GetDeliverySnapshot()
        {
            return new GuaranteedDeliverySnapshot(_deliverySequenceNr, _unconfirmed
                .Select(x => new UnconfirmedDelivery(x.Key, x.Value.Destination, x.Value.Message)));
        }

        public void SetDeliverySnapshot(GuaranteedDeliverySnapshot snapshot)
        {
            _deliverySequenceNr = snapshot.DeliveryId;
            var now = DateTime.UtcNow;
            _unconfirmed = new SortedList<long, Delivery>(snapshot.UnconfirmedDeliveries
                .ToDictionary(x => x.DeliveryId, y => new Delivery(y.Destination, y.Message, now, 0)));
        }

        private void Send(long deliveryId, Delivery delivery, DateTime now)
        {
            _context.ActorSelection(delivery.Destination).Tell(delivery.Message);
            _unconfirmed.Add(deliveryId, delivery.IncrementedCopy());
        }

        private object DeliveryIdToMessage(long deliveryId)
        {
            throw new NotImplementedException();
        }

        private Task RedeliverTask(out CancellationTokenSource cancelationTokenSource)
        {
            cancelationTokenSource = new CancellationTokenSource();
            var interval = TimeSpan.FromTicks(RedeliverInterval.Ticks / 2);
            var task = _context.System.Scheduler.Schedule(interval, interval, _context.Self, RedeliveryTick.Instance, cancelationTokenSource.Token);
            return task;
        }
    }

    public class PersistentGuaranteedDeliveryBase : PersistentActorBase
    {
        private readonly GuaranteedDeliverer _deliverer;

        public PersistentGuaranteedDeliveryBase()
        {
            _deliverer = new GuaranteedDeliverer(Context);
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            if (message is ReplayMessageSuccess)
            {
                _deliverer.RedeliverOverdue();
                return base.AroundReceive(receive, message);
            }
            else if (message is RedeliveryTick)
            {
                _deliverer.RedeliverOverdue();
                return true;
            }
            else
            {
                return base.AroundReceive(receive, message);
            }
        }

        protected override void PreRestart(Exception reason, object message)
        {
            _deliverer.CancelRedelivery();
            base.PreRestart(reason, message);
        }

        protected override void PostStop()
        {
            _deliverer.CancelRedelivery();
            base.PostStop();
        }
    }
}