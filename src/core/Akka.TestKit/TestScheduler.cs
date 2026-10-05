//-----------------------------------------------------------------------
// <copyright file="TestScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.TestKit
{
    /// <summary>
    /// A scheduler whose wall-clock time advances only when a test calls <see cref="Advance(TimeSpan)"/> or <see cref="AdvanceTo(DateTimeOffset)"/>.
    /// </summary>
    public class TestScheduler : IScheduler, IAdvancedScheduler
    {
        private DateTimeOffset _now;
        private readonly ConcurrentDictionary<long, ConcurrentQueue<ScheduledItem>>  _scheduledWork; 

        /// <summary>
        /// Creates a test scheduler initialized to the current UTC time. The configuration and logger parameters are not used.
        /// </summary>
        /// <param name="schedulerConfig">The scheduler configuration required by <see cref="IScheduler"/>.</param>
        /// <param name="log">The logger required by <see cref="IScheduler"/>.</param>
        public TestScheduler(Config schedulerConfig, ILoggingAdapter log)
        {
            _now = DateTimeOffset.UtcNow;
            _scheduledWork = new ConcurrentDictionary<long, ConcurrentQueue<ScheduledItem>>();
        }

        /// <summary>
        /// Advances virtual time and processes the due-time buckets selected for this advance, in due-time order. Callback exceptions propagate and interrupt processing.
        /// </summary>
        /// <param name="offset">The amount of time to add to the scheduler's current time.</param>
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

                _scheduledWork.TryRemove(t.Key, out var removed);

                foreach (var i in removed.Where(r => r.Repeating && (r.Cancelable == null || !r.Cancelable.IsCancellationRequested)))
                {
                    InternalSchedule(null, i.Delay, i.Receiver, i.Message, i.Action, i.Sender, i.Cancelable, i.DeliveryCount);
                }
            }
            
        }

        /// <summary>
        /// Advances virtual time to the specified instant and processes the due-time buckets selected for this advance, in due-time order. Callback exceptions propagate and interrupt processing.
        /// </summary>
        /// <param name="when">The target virtual time, which must not precede the current time.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown when the specified <paramref name="when"/> offset is less than the currently tracked time.
        /// </exception>
        public void AdvanceTo(DateTimeOffset when)
        {
            if (when < _now)
                throw new InvalidOperationException("You can't reverse time...");

            Advance(when.Subtract(_now));
        }

        private void  InternalSchedule(TimeSpan? initialDelay, TimeSpan delay, ICanTell receiver, object message, Action action,
            IActorRef sender, ICancelable cancelable, int deliveryCount = 0)
        {
            var scheduledTime = _now.Add(initialDelay ?? delay).UtcTicks;

            ConcurrentQueue<ScheduledItem> tickItems;
            while (!_scheduledWork.TryGetValue(scheduledTime, out tickItems))
            {
                tickItems = new ConcurrentQueue<ScheduledItem>();
                if (_scheduledWork.TryAdd(scheduledTime, tickItems))
                {
                    break;
                }
            }
            
            var type = message == null ? ScheduledItem.ScheduledItemType.Action : ScheduledItem.ScheduledItemType.Message;

            tickItems.Enqueue(new ScheduledItem(initialDelay ?? delay, delay, type, message, action,
                initialDelay.HasValue || deliveryCount > 0, receiver, sender, cancelable));
        }

        /// <summary>
        /// Schedules a message to be sent after the specified delay when virtual time advances to its due time.
        /// </summary>
        /// <param name="delay">The time from the current virtual time until delivery.</param>
        /// <param name="receiver">The recipient of the message.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender supplied with the message.</param>
        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        {
            InternalSchedule(null, delay, receiver, message, null, sender, null);
        }

        /// <summary>
        /// Schedules a message after the specified delay; the cancellation handle is checked before delivery when due work is processed.
        /// </summary>
        /// <param name="delay">The time from the current virtual time until delivery.</param>
        /// <param name="receiver">The recipient of the message.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender supplied with the message.</param>
        /// <param name="cancelable">The cancellation handle checked before delivery.</param>
        public void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(null, delay, receiver, message, null, sender, cancelable);
        }

        /// <summary>
        /// Schedules a message after <paramref name="initialDelay"/> and once per advance when due, then reschedules it from the current virtual time using <paramref name="interval"/>.
        /// </summary>
        /// <param name="initialDelay">The delay before the first delivery.</param>
        /// <param name="interval">The delay between subsequent deliveries.</param>
        /// <param name="receiver">The recipient of each message.</param>
        /// <param name="message">The message to send on each delivery.</param>
        /// <param name="sender">The sender supplied with each message.</param>
        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender)
        {
            InternalSchedule(initialDelay, interval, receiver, message, null, sender, null);
        }

        /// <summary>
        /// Schedules a message after <paramref name="initialDelay"/> and once per advance when due, then reschedules it from the current virtual time using <paramref name="interval"/> unless canceled before delivery.
        /// </summary>
        /// <param name="initialDelay">The delay before the first delivery.</param>
        /// <param name="interval">The delay between subsequent deliveries.</param>
        /// <param name="receiver">The recipient of each message.</param>
        /// <param name="message">The message to send on each delivery.</param>
        /// <param name="sender">The sender supplied with each message.</param>
        /// <param name="cancelable">The cancellation handle checked before each delivery.</param>
        public void ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
            IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, receiver, message, null, sender, cancelable);
        }

        /// <summary>
        /// Schedules an action to run after the specified delay when virtual time advances to its due time.
        /// </summary>
        /// <param name="delay">The time from the current virtual time until execution.</param>
        /// <param name="action">The action to run.</param>
        /// <param name="cancelable">This implementation does not apply this parameter to the scheduled action.</param>
        public void ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            InternalSchedule(null, delay, null, null, action, null, null);
        }

        /// <summary>
        /// Schedules an action to run after the specified delay when virtual time advances to its due time.
        /// </summary>
        /// <param name="delay">The time from the current virtual time until execution.</param>
        /// <param name="action">The action to run.</param>
        public void ScheduleOnce(TimeSpan delay, Action action)
        {
            InternalSchedule(null, delay, null, null, action, null, null);
        }

        /// <summary>
        /// Schedules an action after <paramref name="initialDelay"/> and once per advance when due, then reschedules it from the current virtual time using <paramref name="interval"/>.
        /// </summary>
        /// <param name="initialDelay">The delay before the first execution.</param>
        /// <param name="interval">The delay between subsequent executions.</param>
        /// <param name="action">The action to run.</param>
        /// <param name="cancelable">The cancellation handle checked before each execution.</param>
        public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, null, null, action, null, cancelable);
        }

        /// <summary>
        /// Schedules an action after <paramref name="initialDelay"/> and once per advance when due, then reschedules it from the current virtual time using <paramref name="interval"/>.
        /// </summary>
        /// <param name="initialDelay">The delay before the first execution.</param>
        /// <param name="interval">The delay between subsequent executions.</param>
        /// <param name="action">The action to run.</param>
        public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            InternalSchedule(initialDelay, interval, null, null, action, null, null);
        }

        /// <summary>
        /// Gets the scheduler's current virtual wall-clock time.
        /// </summary>
        protected DateTimeOffset TimeNow { get { return _now; } }
        /// <summary>
        /// Gets the scheduler's current virtual wall-clock time.
        /// </summary>
        public DateTimeOffset Now { get { return _now; } }
        /// <summary>
        /// Gets the elapsed monotonic time from the process-wide clock; this value is not advanced by <see cref="Advance(TimeSpan)"/>.
        /// </summary>
        public TimeSpan MonotonicClock { get { return Util.MonotonicClock.Elapsed; } }
        /// <summary>
        /// Gets the high-resolution elapsed monotonic time from the process-wide clock; this value is not advanced by <see cref="Advance(TimeSpan)"/>.
        /// </summary>
        public TimeSpan HighResMonotonicClock { get { return Util.MonotonicClock.ElapsedHighRes; } }

        /// <summary>
        /// Gets this scheduler as its advanced scheduling interface.
        /// </summary>
        public IAdvancedScheduler Advanced
        {
            get { return this; }
        }

         /// <summary>
         /// Stores the details needed to deliver a scheduled message or run a scheduled action.
         /// </summary>
       internal class ScheduledItem
        {
            /// <summary>
            /// Gets or sets the delay before the first scheduled delivery or execution.
            /// </summary>
            public TimeSpan InitialDelay { get; set; }
            /// <summary>
            /// Gets or sets the repeat interval or one-time delay.
            /// </summary>
            public TimeSpan Delay { get; set; }
            /// <summary>
            /// Gets or sets whether the item represents a message delivery or an action.
            /// </summary>
            public ScheduledItemType Type { get; set; }
            /// <summary>
            /// Gets or sets the message delivered by this item.
            /// </summary>
            public object Message { get; set; }
            /// <summary>
            /// Gets or sets the action run by this item.
            /// </summary>
            public Action Action { get; set; }
            /// <summary>
            /// Gets or sets whether this item is rescheduled after delivery.
            /// </summary>
            public bool Repeating { get; set; }
            /// <summary>
            /// Gets or sets the message recipient.
            /// </summary>
            public ICanTell Receiver { get; set; }
            /// <summary>
            /// Gets or sets the sender supplied with the message.
            /// </summary>
            public IActorRef Sender { get; set; }
            /// <summary>
            /// Gets or sets the cancellation handle checked before delivery.
            /// </summary>
            public ICancelable Cancelable { get; set; }
            /// <summary>
            /// Gets or sets the number of times this item has been delivered or executed.
            /// </summary>
            public int DeliveryCount { get; set; }

            /// <summary>
            /// Identifies whether a scheduled item sends a message or invokes an action.
            /// </summary>
            public enum ScheduledItemType
            {
                /// <summary>
                /// A scheduled message delivery.
                /// </summary>
                Message,
                /// <summary>
                /// A scheduled action invocation.
                /// </summary>
                Action
            }

            /// <summary>
            /// Initializes a scheduled item with its delivery, timing, and cancellation data.
            /// </summary>
            /// <param name="initialDelay">The delay before the first delivery or execution.</param>
            /// <param name="delay">The one-time delay or repeat interval.</param>
            /// <param name="type">Whether the item sends a message or invokes an action.</param>
            /// <param name="message">The message to send, when the item is a message delivery.</param>
            /// <param name="action">The action to invoke, when the item is an action.</param>
            /// <param name="repeating">Whether the item is rescheduled after it runs.</param>
            /// <param name="receiver">The message recipient, when the item sends a message.</param>
            /// <param name="sender">The sender supplied with the message.</param>
            /// <param name="cancelable">The cancellation handle checked before delivery.</param>
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

         // don't need these methods - not used during testing
         public void ScheduleOnce(TimeSpan delay, IRunnable action, ICancelable cancelable)
         {
             throw new NotImplementedException();
         }

         public void ScheduleOnce(TimeSpan delay, IRunnable action)
         {
             throw new NotImplementedException();
         }

         public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, IRunnable action, ICancelable cancelable)
         {
             throw new NotImplementedException();
         }

         public void ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, IRunnable action)
         {
             throw new NotImplementedException();
         }
    }
}
