using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Serialization;

namespace Akka.Persistence
{
    #region Messages

    /// <summary>
    /// <see cref="GuaranteedDeliverySnapshot"/> is a snapshot of the current state presented by deliverer.
    /// Can be retrieved by <see cref="IGuaranteedDeliverer.GetDeliverySnapshot"/> method 
    /// and saved with <see cref="PersistentActorBase.SaveSnapshot"/>. 
    /// <see cref="GuaranteedDeliverySnapshot"/> contains full delivery state, including unconfirmed messages.
    /// It may be serialized using binary data format.
    /// </summary>
    [Serializable]
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

    /// <summary>
    /// <see cref="UnconfirmedWarning"/> message should be sent after 
    /// <see cref="IGuaranteedDeliverer.UnconfirmedAttemptsToWarn"/> limit will be reached.
    /// </summary>
    public struct UnconfirmedWarning
    {
        public UnconfirmedWarning(IEnumerable<UnconfirmedDelivery> unconfirmedDeliveries)
            : this()
        {
            UnconfirmedDeliveries = unconfirmedDeliveries;
        }

        public IEnumerable<UnconfirmedDelivery> UnconfirmedDeliveries { get; set; }
    }

    /// <summary>
    /// <see cref="UnconfirmedDelivery"/> contains details about unconfirmed messages.
    /// It's included inside <see cref="UnconfirmedWarning"/> and <see cref="GuaranteedDeliverySnapshot"/>.
    /// </summary>
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

    #endregion

    /// <summary>
    /// An exception thrown, when <see cref="IGuaranteedDeliverer.MaxUnconfirmedMessages"/> threshold has been exceeded.
    /// </summary>
    public class MaxUnconfirmedMessagesExceededException : AkkaException
    {
        public MaxUnconfirmedMessagesExceededException(string message, Exception cause = null)
            : base(message, cause)
        {
        }
    }

    /// <summary>
    /// This interface exposes functionality of re-sending messages, that has not been confirmed withing configured timeout.
    /// </summary>
    public interface IGuaranteedDeliverer 
    {
        /// <summary>
        /// Interval between next redelivery attempts.
        /// </summary>
        TimeSpan RedeliverInterval { get; }

        /// <summary>
        /// After this number of delivery attempts, <see cref="UnconfirmedWarning"/> should be sent to composing actor.
        /// </summary>
        int UnconfirmedAttemptsToWarn { get; }

        /// <summary>
        /// Maximum number of undelivered messages, that deliverer is allowed to hold in memory.
        /// When that threshold will be exceeded, a <see cref="MaxUnconfirmedMessagesExceededException"/> will be thrown
        /// and deliverer won't accept any following messages.
        /// </summary>
        int MaxUnconfirmedMessages { get; }
        int UnconfirmedMessages { get; }

        /// <summary>
        /// Returns a full internal state of current deliverer. 
        /// It may be saved using <see cref="PersistentActorBase.SaveSnapshot"/> method. During recovery snapshot received 
        /// inside <see cref="SnapshotOffer"/> should be set back to deliverer using <see cref="SetDeliverySnapshot"/> method.
        /// </summary>
        /// <returns></returns>
        GuaranteedDeliverySnapshot GetDeliverySnapshot();

        /// <summary>
        /// This method may be used for recovery delivery state after receiving <see cref="SnapshotOffer"/> containing
        /// <see cref="GuaranteedDeliverySnapshot"/> created previously by <see cref="GetDeliverySnapshot"/>.
        /// </summary>
        /// <param name="snapshot"></param>
        void SetDeliverySnapshot(GuaranteedDeliverySnapshot snapshot);

        /// <summary>
        /// Send the message created by <paramref name="idToMessageMapper"/> function and then delivered to the 
        /// <paramref name="destination"/> actor. It will be trying to re-deliver message until
        /// <see cref="ConfirmDelivery"/> method will be called. Correlation of this message between two 
        /// methods is performed by deliveryId - an input parameter of <see cref="idToMessageMapper"/> function.
        /// This identifier is also passed as one of the message properties.
        /// 
        /// DeliveryId is generated as monotinically increasing sequence, shared between current deliverer and
        /// all possible destinations.
        /// 
        /// While recovering, deliverer won't send any messages. They will be sent later if no matching 
        /// <see cref="ConfirmDelivery"/> method was invoked.
        /// </summary>
        /// <exception cref="MaxUnconfirmedMessagesExceededException">
        /// Thrown when <see cref="UnconfirmedMessages"/> is greater than or equal to <see cref="MaxUnconfirmedMessages"/>.
        /// </exception>
        void Deliver(ActorPath destination, Func<long, object> idToMessageMapper);

        /// <summary>
        /// This method may be invoked in two cases:
        /// <para>1. Message sent by <see cref="Deliver"/> method has been confirmed by the destination actor.</para>
        /// <para>2. To abort re-sending message identified by <paramref name="deliveryId"/>.</para>
        /// </summary>
        /// <returns>
        /// True on first confirmation of message identified by <paramref name="deliveryId"/>.
        /// False on duplicate confirmations.
        /// </returns>
        bool ConfirmDelivery(long deliveryId);
    }

    public class GuaranteedDeliverer : IGuaranteedDeliverer
    {
        private readonly IActorContext _context;
        private readonly CancellationTokenSource _redeliveryTaskCanceler;
        private Task _redeliverTask;

        private long _deliverySequenceNr = 0L;
        private SortedList<long, Delivery> _unconfirmed = new SortedList<long, Delivery>();

        public GuaranteedDeliverer(IActorContext actorContext)
        {
            _context = actorContext;
            var persistence = actorContext.System.GetExtension<PersistenceExtension>();
            if (persistence == null)
            {
                throw new ArgumentException("Provided actor context refers to actor system, which doesn't have necessary persistance extension initialized");
            }

            var persistenceSettings = persistence.Settings;
            RedeliverInterval = persistenceSettings.GuaranteedDelivery.RedeliverInterval;
            UnconfirmedAttemptsToWarn = persistenceSettings.GuaranteedDelivery.UnconfirmedAttemptsToWarn;
            MaxUnconfirmedMessages = persistenceSettings.GuaranteedDelivery.MaxUnconfirmedMessages;

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

    public abstract class PersistentGuaranteedDeliveryBase : PersistentActorBase
    {
        private readonly GuaranteedDeliverer _deliverer;

        protected PersistentGuaranteedDeliveryBase()
        {
            _deliverer = new GuaranteedDeliverer(Context);
        }

        protected override bool AroundReceive(Receive receive, object message)
        {
            if (message is ReplayMessagesSuccess)
            {
                _deliverer.RedeliverOverdue();
                return base.AroundReceive(receive, message);
            }
            
            if (message is RedeliveryTick)
            {
                _deliverer.RedeliverOverdue();
                return true;
            }
            
            return base.AroundReceive(receive, message);
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