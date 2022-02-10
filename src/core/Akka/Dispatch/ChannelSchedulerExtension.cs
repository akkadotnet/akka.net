using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Dispatch
{
    public sealed class ChannelTaskSchedulerProvider : ExtensionIdProvider<ChannelTaskScheduler>
    {
        public override ChannelTaskScheduler CreateExtension(ExtendedActorSystem system)
        {
            return new ChannelTaskScheduler(system);
        }
    }

    /// <summary>
    /// The Taskscheduler holds multiple TaskSchdulers with different priorities
    /// All scheduled work is executed over the regular ThreadPool 
    /// and the execution sequence is depenedened on the priority of the TaskSchduler
    /// 
    /// Priority TaskSchedulers:
    /// High => All queued work is processed before any other priority
    /// Normal => Normal work load, processed until max-work count 
    /// Low => Only processed after normal work load
    /// Idle => only processed when no other work is queued
    /// </summary>
    public sealed class ChannelTaskScheduler : IExtension, IDisposable
    {
        [ThreadStatic]
        private static TaskSchedulerPriority _threadPriority = TaskSchedulerPriority.None;

        private readonly Task _controlTask; //the main worker
        private readonly CancellationTokenSource _cts = new CancellationTokenSource(); //main cancellation token
        private readonly Timer _timer; //timer to schedule coworkers
        private readonly Task[] _coworkers; //the coworkers
        private readonly int _maximumConcurrencyLevel; //max count of workers
        private readonly int _maxWork = 10; //max executed work items in sequence until priority loop

        private readonly int _workInterval = 500; //time target of executed work items in ms
        private readonly int _workStep = 2; //target work item count in interval / burst

        //priority task schedulers
        private readonly PriorityTaskScheduler _highScheduler;
        private readonly PriorityTaskScheduler _normalScheduler;
        private readonly PriorityTaskScheduler _lowScheduler;
        private readonly PriorityTaskScheduler _idleScheduler;

        public TaskScheduler High => _highScheduler;
        public TaskScheduler Normal => _normalScheduler;
        public TaskScheduler Low => _lowScheduler;
        public TaskScheduler Idle => _idleScheduler;

        public static ChannelTaskScheduler Get(ActorSystem system)
        {
            return system.WithExtension<ChannelTaskScheduler>(typeof(ChannelTaskSchedulerProvider));
        }

        public ChannelTaskScheduler(ExtendedActorSystem system)
        {
            //config channel-scheduler
            var config = system.Settings.Config.GetConfig("akka.channel-scheduler");
            _maximumConcurrencyLevel = ThreadPoolConfig.ScaledPoolSize(
                        config.GetInt("parallelism-min"),
                        config.GetDouble("parallelism-factor", 1.0D), // the scalar-based factor to scale the threadpool size to 
                        config.GetInt("parallelism-max"));
            _maximumConcurrencyLevel = Math.Max(_maximumConcurrencyLevel, 1);
            _maxWork = Math.Max(config.GetInt("work-max", _maxWork), 3); //min 3 normal work in work-loop

            _workInterval = config.GetInt("work-interval", _workInterval);
            _workStep = config.GetInt("work-step", _workStep);

            //create task schedulers
            var channelOptions = new UnboundedChannelOptions()
            {
                AllowSynchronousContinuations = true,
                SingleReader = _maximumConcurrencyLevel == 1,
                SingleWriter = false
            };

            _highScheduler = new PriorityTaskScheduler(Channel.CreateUnbounded<Task>(channelOptions), TaskSchedulerPriority.AboveNormal);
            _normalScheduler = new PriorityTaskScheduler(Channel.CreateUnbounded<Task>(channelOptions), TaskSchedulerPriority.Normal);
            _lowScheduler = new PriorityTaskScheduler(Channel.CreateUnbounded<Task>(channelOptions), TaskSchedulerPriority.Low);
            _idleScheduler = new PriorityTaskScheduler(Channel.CreateUnbounded<Task>(channelOptions), TaskSchedulerPriority.Idle);

            //prefill coworker array
            _coworkers = new Task[_maximumConcurrencyLevel - 1];
            for (var i = 0; i < _coworkers.Length; i++)
                _coworkers[i] = Task.CompletedTask;

            //init paused timer
            _timer = new Timer(ScheduleCoWorkers, "timer", Timeout.Infinite, Timeout.Infinite);

            //start main worker
            _controlTask = Task.Factory.StartNew(ControlAsync, _cts.Token,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

        /// <summary>
        /// Get highest possible TaskSchdeduler of requested priority  
        /// </summary>
        /// <param name="priority">requested priority</param>
        /// <returns>TaskSchdeduler with highest possible priority</returns>
        /// <exception cref="ArgumentException">priority not supported</exception>
        public TaskScheduler GetScheduler(TaskSchedulerPriority priority)
        {
            switch (priority)
            {
                case TaskSchedulerPriority.Normal:
                    return _normalScheduler;
                case TaskSchedulerPriority.Realtime:
                case TaskSchedulerPriority.High:
                case TaskSchedulerPriority.AboveNormal:
                    return _highScheduler;
                case TaskSchedulerPriority.BelowNormal:
                case TaskSchedulerPriority.Low:
                    return _lowScheduler;
                case TaskSchedulerPriority.Background:
                    //case TaskSchedulerPriority.Idle:
                    return _idleScheduler;
                default:
                    throw new ArgumentException(nameof(priority));
            }
        }

        /// <summary>
        /// The main worker waits for work and schedule coworkers 
        /// </summary>
        /// <returns>main worker</returns>
        private async Task ControlAsync()
        {
            var highReader = _highScheduler.Channel.Reader;
            var normalReader = _normalScheduler.Channel.Reader;
            var lowReader = _lowScheduler.Channel.Reader;
            var idleReader = _idleScheduler.Channel.Reader;

            var readTasks = new Task<bool>[] {
                highReader.WaitToReadAsync().AsTask(),
                normalReader.WaitToReadAsync().AsTask(),
                lowReader.WaitToReadAsync().AsTask(),
                idleReader.WaitToReadAsync().AsTask()
            };

            Task<bool> readTask;

            do
            {
                //schedule coworkers
                ScheduleCoWorkers("control");

                //main worker
                DoWork(0);

                //wait on all coworkers exit
                await Task.WhenAll(_coworkers).ConfigureAwait(false);

                //stop timer
                if (!_cts.IsCancellationRequested)
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);

                //reset read events
                if (readTasks[0].IsCompleted)
                    readTasks[0] = highReader.WaitToReadAsync().AsTask();
                if (readTasks[1].IsCompleted)
                    readTasks[1] = normalReader.WaitToReadAsync().AsTask();
                if (readTasks[2].IsCompleted)
                    readTasks[2] = lowReader.WaitToReadAsync().AsTask();
                if (readTasks[3].IsCompleted)
                    readTasks[3] = idleReader.WaitToReadAsync().AsTask();

                //wait on new work)
                readTask = await Task.WhenAny(readTasks).ConfigureAwait(false);
            }
            while (readTask.Result && !_cts.IsCancellationRequested);
        }

        /// <summary>
        /// Scheduling new coworkers based on queued work count 
        /// and starting the timer to reschedule coworkers periodically 
        /// </summary>
        /// <param name="state">indication if the call is from main worker or the timer</param>
        private void ScheduleCoWorkers(object state)
        {
            var name = (string)state; //control or timer
            int i;

            //total count of queued work items
            var queuedWorkItems = _highScheduler.Channel.Reader.Count
                + _normalScheduler.Channel.Reader.Count
                + _lowScheduler.Channel.Reader.Count
                + _idleScheduler.Channel.Reader.Count;

            //required worker count
            var reqWorkerCount = queuedWorkItems;

            //limit required workers to max
            reqWorkerCount = Math.Min(reqWorkerCount, _maximumConcurrencyLevel);

            //count running workers
            var controlWorkerCount = name == "control" ? 1 : 0;
            var coworkerCount = 0;
            for (i = 0; i < _coworkers.Length; i++)
            {
                if (!_coworkers[i].IsCompleted)
                    coworkerCount++;
            }

            //limit new worker count
            var newWorkerToStart = Math.Min(Math.Max(reqWorkerCount - controlWorkerCount - coworkerCount, 0), _workStep);
            if (newWorkerToStart == 0 && reqWorkerCount > controlWorkerCount && (controlWorkerCount + coworkerCount) < _maximumConcurrencyLevel)
                newWorkerToStart = 1;

            if (newWorkerToStart > 0)
            {
                //start new workers
                for (i = 0; newWorkerToStart > 0 && i < _coworkers.Length; i++)
                {
                    if (_coworkers[i].IsCompleted)
                    {
                        _coworkers[i] = Task.Factory.StartNew(Worker, i + 1, _cts.Token,
                            TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                        newWorkerToStart--;
                    }
                }
            }

            //reschedule on timer
            if (!_cts.IsCancellationRequested)
            {
                /*
                calculate interval slot
                1) if work-interval is 500ms and work-step is 2 then try to schedule new worker after 250ms
                   this allows a single worker (main worker) to process everything in its work-slot
                   without the need of any coworkers. this is in the field the main case
                2) if the call is from the timer and all workers could be scheduled then 
                   assume that all workers are busy for the set work-slot and reschedule much later 
                */
                var interval = controlWorkerCount > 0 || (reqWorkerCount - newWorkerToStart) > 0
                    ? _workInterval / _workStep
                    : _workInterval * _workStep;
                _timer.Change(interval, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Task action to run worker
        /// </summary>
        /// <param name="state">workerId</param>
        private void Worker(object state)
        {
            DoWork((int)state);
        }

        /// <summary>
        /// work loop
        /// </summary>
        /// <param name="workerId">the worker id</param>
        /// <returns></returns>
        private int DoWork(int workerId)
        {
            var highCount = 0; //executed work items in priority high 
            var normalCount = 0; //executed work items in priority normal
            var lowCount = 0; //executed work items in priority low
            var idleCount = 0; //executed work items in priority idle

            int c;
            int rounds = 0; //loop count
            int roundWork; //work count in the current loop
            int roundClean = 0; //clean loop count

            //maybe implement max work count and/or a deadline


            //the work loop
            _threadPriority = TaskSchedulerPriority.Idle;
            try
            {   
                do
                {
                    rounds++;
                    roundWork = 0;

                    //execute all high priority work
                    c = _highScheduler.ExecuteAll();
                    highCount += c;
                    roundWork += c;

                    //execute normal priority work until max work count
                    c = _normalScheduler.ExecuteMany(_maxWork);
                    normalCount += c;
                    roundWork += c;

                    //only when all other priority queues where empty
                    //then execute multiple low priority work
                    c = roundWork > 0
                        ? _lowScheduler.ExecuteSingle()
                        : _lowScheduler.ExecuteMany(_maxWork);
                    lowCount += c;
                    roundWork += c;

                    //only execute background tasks
                    //when the current loop executed no work items
                    if (c == 0)
                    {
                        c = _idleScheduler.ExecuteSingle();
                        idleCount += c;
                        roundWork += c;
                    }

                    //count clean loops
                    roundClean = roundWork == 0 ? roundClean + 1 : 0;
                }
                while (roundClean < 2 && !_cts.IsCancellationRequested);
            }
            catch
            {
                //ignore error
            }
            finally
            {
                _threadPriority = TaskSchedulerPriority.None;
            }

            //worker stopped

            var total = highCount + normalCount + lowCount + idleCount;

            //todo push to metrics: workerId, total, highCount, normalCount, lowCount, idleCount
            //maybe add StopWatch

            return total;
        }

        public void Dispose()
        {
            _idleScheduler.Dispose();
            _lowScheduler.Dispose();
            _normalScheduler.Dispose();
            _highScheduler.Dispose();

            _cts.Cancel();
            _timer.Dispose();
        }

        /// <summary>
        /// TaskScheduler to queue work items to the priority-channel from user space
        /// and to help execute queued works internaly. 
        /// It supports task-inlining only for task equal or above the own priority
        /// </summary>
        internal sealed class PriorityTaskScheduler : TaskScheduler, IDisposable
        {
            readonly Channel<Task> _channel;

            readonly TaskSchedulerPriority _priority;

            public Channel<Task> Channel => _channel;
            public TaskSchedulerPriority Priority => _priority;

            public PriorityTaskScheduler(Channel<Task> channel, TaskSchedulerPriority priority)
            {
                _channel = channel;
                _priority = priority;
            }

            protected override void QueueTask(Task task)
            {
                if (!_channel.Writer.TryWrite(task))
                    throw new InvalidOperationException();
            }

            protected override IEnumerable<Task> GetScheduledTasks()
            {
                return Array.Empty<Task>();
            }

            protected override bool TryDequeue(Task task)
            {
                return false;
            }

            protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
            {
                // If this thread isn't already processing a task
                // and the thread priority is higher, then we don't support inlining 
                return (_threadPriority > TaskSchedulerPriority.None && _threadPriority <= _priority)
                    && TryExecuteTask(task);
            }

            /// <summary>
            /// execute work until unsuccessfull
            /// </summary>
            /// <returns>dequeued work count</returns>
            public int ExecuteAll()
            {
                _threadPriority = _priority;

                var reader = _channel.Reader;
                var count = 0;

                while (reader.TryRead(out var task))
                {
                    count++;  //maybe only count successfully executed
                    if (!TryExecuteTask(task))
                        return count;
                }
                return count;
            }

            /// <summary>
            /// execute work until unsuccessfull or max count
            /// </summary>
            /// <param name="maxTasks">max work to execute</param>
            /// <returns>dequeued work count</returns>
            public int ExecuteMany(int maxTasks)
            {
                _threadPriority = _priority;

                var reader = _channel.Reader;
                int c;

                for (c = 0; c < maxTasks && reader.TryRead(out var task); c++)
                    if (!TryExecuteTask(task))
                        return c + 1;

                return c;
            }

            /// <summary>
            /// execute single work item
            /// </summary>
            /// <returns>dequeued work count</returns>
            public int ExecuteSingle()
            {
                _threadPriority = _priority;

                if (_channel.Reader.TryRead(out var task))
                {
                    TryExecuteTask(task);
                    return 1;
                }
                return 0;
            }

            public void Dispose()
            {
                _channel.Writer.TryComplete();
            }
        }
    }

    /// <summary>
    /// Windows API related Process and Thread Priorities
    /// </summary>
    public enum TaskSchedulerPriority
    {
        None = 0,
        Idle = 4,
        Background = 4,
        Low = 5,
        BelowNormal = 6,
        Normal = 8,
        AboveNormal = 10,
        High = 13,
        Realtime = 24
    }
}
