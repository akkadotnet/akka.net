//-----------------------------------------------------------------------
// <copyright file="DedicatedThreadScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Event;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// Obsolete. Use <see cref="HashedWheelTimerScheduler"/> instead.
    /// </summary>
    [Obsolete("Replaced with HashedWheelTimerScheduler [1.1.2]")]
    public class DedicatedThreadScheduler : SchedulerBase, IDateTimeOffsetNowTimeProvider, IDisposable
    {
        private readonly ConcurrentQueue<ScheduledWork> _workQueue = new ConcurrentQueue<ScheduledWork>();

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.TimeNow"/> instead.
        /// </summary>
        protected override DateTimeOffset TimeNow
        {
            get { return DateTimeOffset.Now; }
        }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.MonotonicClock"/> instead.
        /// </summary>
        public override TimeSpan MonotonicClock
        {
            get { return Util.MonotonicClock.Elapsed; }
        }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.HighResMonotonicClock"/> instead.
        /// </summary>
        public override TimeSpan HighResMonotonicClock
        {
            get { return Util.MonotonicClock.ElapsedHighRes; }
        }

        private TimeSpan _shutdownTimeout;

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler(Config, ILoggingAdapter)"/> instead.
        /// </summary>
        /// <param name="sys">N/A</param>
        [Obsolete("Dangerous and bad. Use DedicatedThreadScheduler(Config config, ILoggingAdapter log) instead. [1.1.2]")]
        public DedicatedThreadScheduler(ActorSystem sys) : this(sys.Settings.Config, sys.Log) { }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler(Config, ILoggingAdapter)"/> instead.
        /// </summary>
        /// <param name="config">N/A</param>
        /// <param name="log">N/A</param>
        public DedicatedThreadScheduler(Config config, ILoggingAdapter log) : base(config, log)
        {
            var precision = SchedulerConfig.GetTimeSpan("akka.scheduler.tick-duration");
            _shutdownTimeout = SchedulerConfig.GetTimeSpan("akka.scheduler.shutdown-timeout");
            var thread = new Thread(_ =>
            {
                var allWork = new List<ScheduledWork>();
                while (_stopped.Value == null)
                {
                    Thread.Sleep(precision);
                    var now = HighResMonotonicClock.Ticks;
                    ScheduledWork work;
                    while (_workQueue.TryDequeue(out work))
                    {
                        //has work already expired?
                        if (work.TickExpires < now)
                        {
                            work.Action();
                        }
                        else
                        {
                            //buffer it for later
                            allWork.Add(work);
                        }
                    }
                    //this is completely stupid, but does work.. 
                    if (allWork.Count > 0)
                    {
                        var tmp = allWork;
                        allWork = new List<ScheduledWork>();
                        foreach (var bufferedWork in tmp)
                        {
                            if (bufferedWork.TickExpires < now)
                            {
                                bufferedWork.Action();
                            }
                            else
                            {
                                allWork.Add(bufferedWork);
                            }
                        }
                    }
                }

                // shutdown has been signaled
               FireStopSignal();
            }) {IsBackground = true};

            thread.Start();
        }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.InternalScheduleTellOnce(TimeSpan, ICanTell, object, IActorRef, ICancelable)"/> instead.
        /// </summary>
        /// <param name="delay">N/A</param>
        /// <param name="receiver">N/A</param>
        /// <param name="message">N/A</param>
        /// <param name="sender">N/A</param>
        /// <param name="cancelable">N/A</param>
        protected override void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message,
            IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, () =>
            {
                receiver.Tell(message, sender);
            }, cancellationToken);
        }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.InternalScheduleTellRepeatedly(TimeSpan, TimeSpan, ICanTell, object, IActorRef, ICancelable)"/> instead.
        /// </summary>
        /// <param name="initialDelay">N/A</param>
        /// <param name="interval">N/A</param>
        /// <param name="receiver">N/A</param>
        /// <param name="message">N/A</param>
        /// <param name="sender">N/A</param>
        /// <param name="cancelable">N/A</param>
        protected override void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval,
            ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(message, sender), cancellationToken);
        }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.InternalScheduleOnce(TimeSpan, Action, ICancelable)"/> instead.
        /// </summary>
        /// <param name="delay">N/A</param>
        /// <param name="action">N/A</param>
        /// <param name="cancelable">N/A</param>
        protected override void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, action, cancellationToken);
        }

        /// <summary>
        /// Obsolete. Use <see cref="HashedWheelTimerScheduler.InternalScheduleRepeatedly(TimeSpan, TimeSpan, Action, ICancelable)"/> instead.
        /// </summary>
        /// <param name="initialDelay">N/A</param>
        /// <param name="interval">N/A</param>
        /// <param name="action">N/A</param>
        /// <param name="cancelable">N/A</param>
        protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action,
            ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleRepeatedly(initialDelay, interval, action, cancellationToken);
        }


        private void InternalScheduleOnce(TimeSpan initialDelay, Action action, CancellationToken token)
        {
            Action executeAction = () =>
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    action();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception x)
                {
                    Log.Error(x, "DedicatedThreadScheduler failed to execute action");
                }
            };
            AddWork(initialDelay, executeAction, token);

        }


        private void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action,
            CancellationToken token)
        {
            Action executeAction = null;
            executeAction = () =>
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    action();
                    if (token.IsCancellationRequested)
                        return;

                    AddWork(interval, executeAction, token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception x)
                {
                    Log.Error(x, "DedicatedThreadScheduler failed to execute action");
                }
            };
            AddWork(initialDelay, executeAction, token);

        }

        private void AddWork(TimeSpan delay, Action work, CancellationToken token)
        {
            if (_stopped.Value != null)
                throw new SchedulerException("cannot enqueue after timer shutdown");
            var expected = HighResMonotonicClock + delay;
            var scheduledWord = new ScheduledWork(expected.Ticks, work, token);
            _workQueue.Enqueue(scheduledWord);
        }

        private AtomicReference<TaskCompletionSource<bool>> _stopped = new AtomicReference<TaskCompletionSource<bool>>();

        private void FireStopSignal()
        {
            try
            {
                _stopped.Value.TrySetResult(true);
            }
            catch (Exception)
            {
                
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!Stop().Wait(_shutdownTimeout))
            {    
                Log.Warning("Failed to shutdown DedicatedThreadScheduler within {0}", _shutdownTimeout);   
            }
        }

        private static readonly Task Completed = Task.FromResult(true);

        private Task Stop()
        {
            var p = new TaskCompletionSource<bool>();
            if (_stopped.CompareAndSet(null, p))
            {
                // Let remaining work that is already being processed finished. The termination task will complete afterwards
                return p.Task;
            }
            return Completed;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ScheduledWork
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tickExpires">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="token">TBD</param>
        public ScheduledWork(long tickExpires, Action action, CancellationToken token)
        {
            TickExpires = tickExpires;
            Action = action;
            Token = token;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public CancellationToken Token { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public long TickExpires { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action Action { get; set; }
    }
}
