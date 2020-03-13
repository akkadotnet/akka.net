//-----------------------------------------------------------------------
// <copyright file="TestScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public class TestScheduler : IScheduler, IAdvancedScheduler
    {
        private DateTimeOffset _now;
        private readonly ConcurrentDictionary<long, ConcurrentQueue<ScheduledItem>>  _scheduledWork; 

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="schedulerConfig">TBD</param>
        /// <param name="log">TBD</param>
        public TestScheduler(Config schedulerConfig, ILoggingAdapter log)
        {
            _now = DateTimeOffset.UtcNow;
            _scheduledWork = new ConcurrentDictionary<long, ConcurrentQueue<ScheduledItem>>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="offset">TBD</param>
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

                ConcurrentQueue<ScheduledItem> removed;
                _scheduledWork.TryRemove(t.Key, out removed);

                foreach (var i in removed.Where(r => r.Repeating && (r.Cancelable == null || !r.Cancelable.IsCancellationRequested)))
                {
                    InternalSchedule(null, i.Delay, i.Receiver, i.Message, i.Action, i.Sender, i.Cancelable, i.DeliveryCount);
                }
            }
            
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="when">TBD</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown when the specified <paramref name="when"/> offset is less than the currently tracked time.
        /// </exception>
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

            if (!_scheduledWork.TryGetValue(scheduledTime, out var tickItems))
            {
                tickItems = new ConcurrentQueue<ScheduledItem>();
                _scheduledWork.TryAdd(scheduledTime, tickItems);
            }
            
            var type = message == null ? ScheduledItem.ScheduledItemType.Action : ScheduledItem.ScheduledItemType.Message;

            tickItems.Enqueue(new ScheduledItem(initialDelay ?? delay, delay, type, message, action,
                initialDelay.HasValue || deliveryCount > 0, receiver, sender, cancelable));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        {
            InternalSchedule(null, delay, receiver, message, null, sender, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(null, delay, receiver, message, null, sender, cancelable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender)
        {
            InternalSchedule(initialDelay, interval, receiver, message, null, sender, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, receiver, message, null, sender, cancelable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        public void ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            InternalSchedule(null, delay, null, null, action, null, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        public void ScheduleOnce(TimeSpan delay, Action action)
        {
            InternalSchedule(null, delay, null, null, action, null, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, null, null, action, null, cancelable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            InternalSchedule(initialDelay, interval, null, null, action, null, null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected DateTimeOffset TimeNow { get { return _now; } }
        /// <summary>
        /// TBD
        /// </summary>
        public DateTimeOffset Now { get { return _now; } }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan MonotonicClock { get { return Util.MonotonicClock.Elapsed; } }
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan HighResMonotonicClock { get { return Util.MonotonicClock.ElapsedHighRes; } }

        /// <summary>
        /// TBD
        /// </summary>
        public IAdvancedScheduler Advanced
        {
            get { return this; }
        }

         /// <summary>
        /// TBD
        /// </summary>
       internal class ScheduledItem
        {
            /// <summary>
            /// TBD
            /// </summary>
            public TimeSpan InitialDelay { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public TimeSpan Delay { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public ScheduledItemType Type { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public object Message { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public Action Action { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public bool Repeating { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public ICanTell Receiver { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Sender { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public ICancelable Cancelable { get; set; }
            /// <summary>
            /// TBD
            /// </summary>
            public int DeliveryCount { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            public enum ScheduledItemType
            {
                /// <summary>
                /// TBD
                /// </summary>
                Message,
                /// <summary>
                /// TBD
                /// </summary>
                Action
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="initialDelay">TBD</param>
            /// <param name="delay">TBD</param>
            /// <param name="type">TBD</param>
            /// <param name="message">TBD</param>
            /// <param name="action">TBD</param>
            /// <param name="repeating">TBD</param>
            /// <param name="receiver">TBD</param>
            /// <param name="sender">TBD</param>
            /// <param name="cancelable">TBD</param>
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
