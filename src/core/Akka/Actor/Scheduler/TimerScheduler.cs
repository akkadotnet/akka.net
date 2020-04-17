using Akka.Event;
using Akka.Util.Internal;
using System;
using System.Collections.Generic;

namespace Akka.Actor.Scheduler
{
    /// <summary>
    /// Support for scheduled "Self" messages in an actor.
    ///
    /// Timers are bound to the lifecycle of the actor that owns it,
    /// and thus are cancelled automatically when it is restarted or stopped.
    ///
    /// <see cref="ITimerScheduler"/> is not thread-safe, i.e.it must only be used within
    /// the actor that owns it.
    /// </summary>
    internal class TimerScheduler : ITimerScheduler
    {
        private class Timer
        {
            public object Key { get; }
            public object Msg { get; }
            public bool Repeat { get; }
            public int Generation { get; }
            public ICancelable Task { get; }

            public Timer(object key, object msg, bool repeat, int generation, ICancelable task)
            {
                this.Key = key;
                this.Msg = msg;
                this.Repeat = repeat;
                this.Generation = generation;
                this.Task = task;
            }
        }

        public interface ITimerMsg
        {
            object Key { get; }
            int Generation { get; }
            TimerScheduler Owner { get; }
        }

        private class TimerMsg : ITimerMsg, INoSerializationVerificationNeeded
        {
            public object Key { get; }
            public int Generation { get; }
            public TimerScheduler Owner { get; }

            public TimerMsg(object key, int generation, TimerScheduler owner)
            {
                this.Key = key;
                this.Generation = generation;
                this.Owner = owner;
            }

            public override string ToString()
            {
                return $"TimerMsg(key={Key}, generation={Generation}, owner={Owner})";
            }
        }

        private class TimerMsgNotInfluenceReceiveTimeout : TimerMsg, INotInfluenceReceiveTimeout
        {
            public TimerMsgNotInfluenceReceiveTimeout(object key, int generation, TimerScheduler owner)
                : base(key, generation, owner)
            {
            }
        }

        private readonly IActorContext ctx;
        private readonly Dictionary<object, Timer> timers = new Dictionary<object, Timer>();
        private AtomicCounter timerGen = new AtomicCounter(0);


        public TimerScheduler(IActorContext ctx)
        {
            this.ctx = ctx;
        }

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="interval">Interval</param>
        public void StartPeriodicTimer(object key, object msg, TimeSpan interval)
        {
            StartTimer(key, msg, interval, interval, true);
        }

        /// <summary>
        /// Start a periodic timer that will send <paramref name="msg"/> to the "Self" actor at
        /// a fixed <paramref name="interval"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="initialDelay">Initial delay</param>
        /// <param name="interval">Interval</param>
        public void StartPeriodicTimer(object key, object msg, TimeSpan initialDelay, TimeSpan interval)
        {
            StartTimer(key, msg, interval, initialDelay, true);
        }

        /// <summary>
        /// Start a timer that will send <paramref name="msg"/> once to the "Self" actor after
        /// the given <paramref name="timeout"/>.
        ///
        /// Each timer has a key and if a new timer with same key is started
        /// the previous is cancelled and it's guaranteed that a message from the
        /// previous timer is not received, even though it might already be enqueued
        /// in the mailbox when the new timer is started.
        /// </summary>
        /// <param name="key">Name of timer</param>
        /// <param name="msg">Message to schedule</param>
        /// <param name="timeout">Interval</param>
        public void StartSingleTimer(object key, object msg, TimeSpan timeout)
        {
            StartTimer(key, msg, timeout, TimeSpan.Zero, false);
        }

        /// <summary>
        /// Check if a timer with a given <paramref name="key"/> is active.
        /// </summary>
        /// <param name="key"></param>
        /// <returns>Name of timer</returns>
        public bool IsTimerActive(object key)
        {
            return timers.ContainsKey(key);
        }

        /// <summary>
        /// Cancel a timer with a given <paramref name="key"/>.
        /// If canceling a timer that was already canceled, or key never was used to start a timer
        /// this operation will do nothing.
        ///
        /// It is guaranteed that a message from a canceled timer, including its previous incarnation
        /// for the same key, will not be received by the actor, even though the message might already
        /// be enqueued in the mailbox when cancel is called.
        /// </summary>
        /// <param name="key">Name of timer</param>
        public void Cancel(object key)
        {
            if (timers.TryGetValue(key, out var timer))
                CancelTimer(timer);
        }

        /// <summary>
        /// Cancel all timers.
        /// </summary>
        public void CancelAll()
        {
            ctx.System.Log.Debug("Cancel all timers");
            foreach (var timer in timers)
                timer.Value.Task.Cancel();
            timers.Clear();
        }

        private void CancelTimer(Timer timer)
        {
            ctx.System.Log.Debug("Cancel timer [{0}] with generation [{1}]", timer.Key, timer.Generation);
            timer.Task.Cancel();
            timers.Remove(timer.Key);
        }


        private void StartTimer(object key, object msg, TimeSpan timeout, TimeSpan initialDelay, bool repeat)
        {
            if (timers.TryGetValue(key, out var timer))
                CancelTimer(timer);

            var nextGen = timerGen.Next();

            ITimerMsg timerMsg;
            if (msg is INotInfluenceReceiveTimeout)
                timerMsg = new TimerMsgNotInfluenceReceiveTimeout(key, nextGen, this);
            else
                timerMsg = new TimerMsg(key, nextGen, this);

            ICancelable task;
            if (repeat)
                task = ctx.System.Scheduler.ScheduleTellRepeatedlyCancelable(initialDelay, timeout, ctx.Self, timerMsg, ActorRefs.NoSender);
            else
                task = ctx.System.Scheduler.ScheduleTellOnceCancelable(timeout, ctx.Self, timerMsg, ActorRefs.NoSender);

            var nextTimer = new Timer(key, msg, repeat, nextGen, task);
            ctx.System.Log.Debug("Start timer [{0}] with generation [{1}]", key, nextGen);
            timers[key] = nextTimer;
        }

        public object InterceptTimerMsg(ILoggingAdapter log, ITimerMsg timerMsg)
        {
            if (!timers.TryGetValue(timerMsg.Key, out var timer))
            {
                // it was from canceled timer that was already enqueued in mailbox
                log.Debug("Received timer [{0}] that has been removed, discarding", timerMsg.Key);
                return null; // message should be ignored
            }
            if (!ReferenceEquals(timerMsg.Owner, this))
            {
                // after restart, it was from an old instance that was enqueued in mailbox before canceled
                log.Debug("Received timer [{0}] from old restarted instance, discarding", timerMsg.Key);
                return null; // message should be ignored
            }
            else if (timerMsg.Generation == timer.Generation)
            {
                // valid timer
                if (!timer.Repeat)
                    timers.Remove(timer.Key);
                return timer.Msg;
            }
            else
            {
                // it was from an old timer that was enqueued in mailbox before canceled
                log.Debug(
                    "Received timer [{0}] from old generation [{1}], expected generation [{2}], discarding",
                    timerMsg.Key,
                    timerMsg.Generation,
                    timer.Generation);
                return null; // message should be ignored
            }
        }
    }
}
