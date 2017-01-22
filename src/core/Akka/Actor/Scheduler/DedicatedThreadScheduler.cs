﻿//-----------------------------------------------------------------------
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
    /// TBD
    /// </summary>
    [Obsolete("Replaced with HashedWheelTimerScheduler")]
    public class DedicatedThreadScheduler : SchedulerBase, IDateTimeOffsetNowTimeProvider, IDisposable
    {
        private readonly ConcurrentQueue<ScheduledWork> _workQueue = new ConcurrentQueue<ScheduledWork>();

        /// <summary>
        /// TBD
        /// </summary>
        protected override DateTimeOffset TimeNow
        {
            get { return DateTimeOffset.Now; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override TimeSpan MonotonicClock
        {
            get { return Util.MonotonicClock.Elapsed; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override TimeSpan HighResMonotonicClock
        {
            get { return Util.MonotonicClock.ElapsedHighRes; }
        }

        private TimeSpan _shutdownTimeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sys">TBD</param>
        [Obsolete("Dangerous and bad. Use DedicatedThreadScheduler(Config config, ILoggingAdapter log) instead.")]
        public DedicatedThreadScheduler(ActorSystem sys) : this(sys.Settings.Config, sys.Log) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="log">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected override void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval,
            ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(message, sender), cancellationToken);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected override void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, action, cancellationToken);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
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
                throw new SchedulerException("cannot enque after timer shutdown");
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

        /// <summary>
        /// TBD
        /// </summary>
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
        public ScheduledWork(long tickExpires, Action action,CancellationToken token)
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
