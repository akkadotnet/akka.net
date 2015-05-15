using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Akka.Actor
{
    public class DedicatedThreadScheduler : SchedulerBase, IDateTimeOffsetNowTimeProvider
    {
        private readonly ConcurrentQueue<ScheduledWork> _workQueue = new ConcurrentQueue<ScheduledWork>();  
        protected override DateTimeOffset TimeNow { get { return DateTimeOffset.Now; } }
        public override TimeSpan MonotonicClock { get { return Util.MonotonicClock.Elapsed; } }
        public override TimeSpan HighResMonotonicClock { get { return Util.MonotonicClock.ElapsedHighRes; } }

        //TODO: use some more efficient approach to handle future work
        public DedicatedThreadScheduler(ActorSystem system)
        {
            var precision = system.Settings.Config.GetTimeSpan("akka.scheduler.tick-duration");
            var thread = new Thread(_ =>
            {
                var allWork = new List<ScheduledWork>();
                while (true)
                {
                    if (system.TerminationTask.IsCompleted)
                    {
                        return;
                    }

                    Thread.Sleep(precision);
                    var now = HighResMonotonicClock.Ticks;
                    ScheduledWork work;
                    while(_workQueue.TryDequeue(out work))
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
            }) {IsBackground = true};

            thread.Start();
        }

        protected override void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, () =>
            {
                receiver.Tell(message, sender);
            }, cancellationToken);
        }

        protected override void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleRepeatedly(initialDelay, interval, () => receiver.Tell(message, sender), cancellationToken);
        }

        protected override void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            var cancellationToken = cancelable == null ? CancellationToken.None : cancelable.Token;
            InternalScheduleOnce(delay, action, cancellationToken);
        }

        protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
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
                catch (OperationCanceledException) { }
                //TODO: Should we log other exceptions? /@hcanber
            };
            AddWork(initialDelay, executeAction, token);

        }


        private void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, CancellationToken token)
        {
            Action executeAction = null;
            executeAction = () =>
            {
                if (token.IsCancellationRequested) 
                    return;

                try
                {
                    action();
                }
                catch (OperationCanceledException) { }
                //TODO: Should we log other exceptions? /@hcanber

                if (token.IsCancellationRequested) 
                    return;

                AddWork(interval, executeAction,token);
               
            };
            AddWork(initialDelay, executeAction, token);

        }

        private void AddWork(TimeSpan delay, Action work,CancellationToken token)
        {
            var expected = HighResMonotonicClock + delay;
            var scheduledWord = new ScheduledWork(expected.Ticks, work,token);
            _workQueue.Enqueue(scheduledWord);
        }               
    }

    public class ScheduledWork
    {
        public ScheduledWork(long tickExpires, Action action,CancellationToken token)
        {
            TickExpires = tickExpires;
            Action = action;
            Token = token;
        }

        public CancellationToken Token { get; set; }
        public long TickExpires { get; set; }
        public Action Action { get; set; }
    }
}
