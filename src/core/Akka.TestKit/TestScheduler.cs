using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.TestKit
{
    public class TestScheduler : IScheduler,
                                 IAdvancedScheduler
    {
        private DateTimeOffset _now;
        private readonly ConcurrentDictionary<long, Queue<ScheduledItem>>  _scheduledWork; 

        public TestScheduler(ActorSystem system)
        {
            _now = DateTimeOffset.UtcNow;
            _scheduledWork = new ConcurrentDictionary<long, Queue<ScheduledItem>>();
        }

        public void Advance(TimeSpan offset)
        {
            _now = _now.Add(offset);

            var tickItems = _scheduledWork.Where(s => s.Key <= _now.Ticks).OrderBy(s => s.Key).ToList();

            foreach (var t in tickItems)
            {
                foreach (var si in t.Value.Where(i => i.Cancelable == null || !i.Cancelable.IsCancellationRequested))
                {
                    if (si.Type == ScheduledItem.ScheduledItemType.Message)
                        si.Receiver.Tell(si.Message, si.Sender);
                    else
                        si.Action();

                    si.DeliveryCount++;
                }

                Queue<ScheduledItem> removed;
                _scheduledWork.TryRemove(t.Key, out removed);

                foreach (var i in removed.Where(r => r.Repeating && (r.Cancelable == null || !r.Cancelable.IsCancellationRequested)))
                {
                    InternalSchedule(null, i.Delay, i.Receiver, i.Message, i.Action, i.Sender, i.Cancelable, i.DeliveryCount);
                }
            }
            
        }

        public void AdvanceTo(DateTimeOffset when)
        {
            if (when < _now)
                throw new InvalidOperationException("You can't reverse time...");

            Advance(when.Subtract(_now));
        }

        private void InternalSchedule(TimeSpan? initialDelay, TimeSpan delay, ICanTell receiver, object message, Action action,
            IActorRef sender, ICancelable cancelable, int deliveryCount = 0)
        {
            var scheduledTime = _now.Add(initialDelay ?? delay).UtcTicks;

            Queue<ScheduledItem> tickItems = null;
            if (!_scheduledWork.TryGetValue(scheduledTime, out tickItems))
            {
                tickItems = new Queue<ScheduledItem>();
                _scheduledWork.TryAdd(scheduledTime, tickItems);
            }
            
            var type = message == null ? ScheduledItem.ScheduledItemType.Action : ScheduledItem.ScheduledItemType.Message;

            tickItems.Enqueue(new ScheduledItem(initialDelay ?? delay, delay, type, message, action,
                initialDelay.HasValue || deliveryCount > 0, receiver, sender, cancelable));
        }


        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        {
            InternalSchedule(null, delay, receiver, message, null, sender, null);
        }

        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(null, delay, receiver, message, null, sender, cancelable);
        }

        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender)
        {
            InternalSchedule(initialDelay, interval, receiver, message, null, sender, null);
        }

        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, receiver, message, null, sender, cancelable);
        }


        public void ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            InternalSchedule(null, delay, null, null, action, null, null);
        }

        public void ScheduleOnce(TimeSpan delay, Action action)
        {
            InternalSchedule(null, delay, null, null, action, null, null);

        }

        public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, null, null, action, null, cancelable);
        }

        public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            InternalSchedule(initialDelay, interval, null, null, action, null, null);
        }

        protected DateTimeOffset TimeNow { get { return _now; } }
        public DateTimeOffset Now { get { return _now; } }
        public TimeSpan MonotonicClock { get { return Util.MonotonicClock.Elapsed; } }
        public TimeSpan HighResMonotonicClock { get { return Util.MonotonicClock.ElapsedHighRes; } }

        public IAdvancedScheduler Advanced
        {
            get { return this; }
        }

        internal class ScheduledItem
        {
            public TimeSpan InitialDelay { get; set; }
            public TimeSpan Delay { get; set; }
            public ScheduledItemType Type { get; set; }
            public object Message { get; set; }
            public Action Action { get; set; }
            public bool Repeating { get; set; }
            public ICanTell Receiver { get; set; }
            public IActorRef Sender { get; set; }
            public ICancelable Cancelable { get; set; }
            public int DeliveryCount { get; set; }

            public enum ScheduledItemType
            {
                Message, Action
            }

            public ScheduledItem(TimeSpan initialDelay, TimeSpan delay, ScheduledItemType type, object message, Action action, bool repeating, ICanTell receiver, 
                IActorRef sender, ICancelable cancelable)
            {
                InitialDelay = initialDelay;
                Delay = delay;
                Type = type;
                Message = message;
                Action = action;
                Repeating = repeating;
                Receiver = receiver;
                Sender = sender;
                Cancelable = cancelable;
                DeliveryCount = 0;
            }
        }

    }
}
