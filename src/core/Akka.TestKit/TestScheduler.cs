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
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public class TestScheduler : IScheduler, IAdvancedScheduler
    {
        // UTC ticks of the virtual clock. Written only while holding _gate; read without a lock via Interlocked.Read
        private long _nowTicks;
        private readonly ConcurrentDictionary<long, ConcurrentQueue<ScheduledItem>>  _scheduledWork;

        // Serializes Advance and scheduling. Monitor is reentrant, so a scheduled action may call Advance or schedule
        // more work on the same thread.
        private readonly object _gate = new();

        private DateTimeOffset _now => new(Interlocked.Read(ref _nowTicks), TimeSpan.Zero);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="schedulerConfig">TBD</param>
        /// <param name="log">TBD</param>
        public TestScheduler(Config schedulerConfig, ILoggingAdapter log)
        {
            _nowTicks = DateTimeOffset.UtcNow.UtcTicks;
            _scheduledWork = new ConcurrentDictionary<long, ConcurrentQueue<ScheduledItem>>();
        }

        /// <summary>
        /// Moves the virtual clock forward by <paramref name="offset"/> and runs every item that is now due.
        /// </summary>
        /// <param name="offset">How far to move the clock.</param>
        /// <remarks>
        /// Safe to call from several threads at once, and from inside a scheduled action. Each due item runs
        /// exactly once. Scheduled actions and <c>Tell</c> calls run while an internal lock is held, so an action
        /// must not block on another thread that is calling <see cref="Advance"/> or scheduling work. If an action
        /// throws, the exception propagates and the remaining items in that due-time bucket do not run.
        /// </remarks>
        public void Advance(TimeSpan offset)
        {
            lock (_gate)
            {
                var now = _now.Add(offset);
                Interlocked.Exchange(ref _nowTicks, now.UtcTicks);

                var dueKeys = _scheduledWork.Keys.Where(k => k <= now.UtcTicks).OrderBy(k => k).ToList();

                foreach (var key in dueKeys)
                {
                    // Claim the slot before running it. A nested Advance can't see it again, and a slot already
                    // claimed by a nested call is skipped.
                    if (!_scheduledWork.TryRemove(key, out var queue))
                        continue;

                    var items = queue.ToArray();

                    foreach (var si in items.Where(i => i.Cancelable == null || !i.Cancelable.IsCancellationRequested))
                    {
                        if (si.Type == ScheduledItem.ScheduledItemType.Message)
                            si.Receiver.Tell(si.Message, si.Sender);
                        else
                            si.Action();

                        si.DeliveryCount++;
                    }

                    foreach (var i in items.Where(r => r.Repeating && (r.Cancelable == null || !r.Cancelable.IsCancellationRequested)))
                    {
                        InternalSchedule(null, i.Delay, i.Receiver, i.Message, i.Action, i.Sender, i.Cancelable, i.DeliveryCount);
                    }
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
            lock (_gate)
            {
                if (when < _now)
                    throw new InvalidOperationException("You can't reverse time...");

                Advance(when.Subtract(_now));
            }
        }

        private void  InternalSchedule(TimeSpan? initialDelay, TimeSpan delay, ICanTell receiver, object message, Action action,
            IActorRef sender, ICancelable cancelable, int deliveryCount = 0)
        {
            var type = message == null ? ScheduledItem.ScheduledItemType.Action : ScheduledItem.ScheduledItemType.Message;

            // Hold the lock so we can't enqueue into a slot that Advance has just claimed and removed.
            lock (_gate)
            {
                var scheduledTime = _now.Add(initialDelay ?? delay).UtcTicks;

                var tickItems = _scheduledWork.GetOrAdd(scheduledTime, _ => new ConcurrentQueue<ScheduledItem>());

                tickItems.Enqueue(new ScheduledItem(initialDelay ?? delay, delay, type, message, action,
                    initialDelay.HasValue || deliveryCount > 0, receiver, sender, cancelable));
            }
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
