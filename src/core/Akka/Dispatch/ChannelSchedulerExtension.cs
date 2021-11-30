using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Akka.Dispatch
{
    public sealed class ChannelTaskSchedulerProvider : ExtensionIdProvider<ChannelTaskScheduler>
    {
        public override ChannelTaskScheduler CreateExtension(ExtendedActorSystem system)
        {
            return new ChannelTaskScheduler(system);
        }
    }

    public sealed class ChannelTaskScheduler : IExtension, IDisposable
    {
        [ThreadStatic]
        private static TaskSchedulerPriority _threadPriority = TaskSchedulerPriority.None;

        private readonly Task _controlTask;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Timer _timer;
        private readonly Task[] _coworkers;
        private readonly int _maximumConcurrencyLevel;
        private readonly int _maxWork = 3; //max work items to execute at one priority

        private readonly int _workInterval = 500;
        private readonly int _workStep = 2;

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
            //todo own channel-task-scheduler config section
            var config = system.Settings.Config.GetConfig("akka.channel-scheduler");
            _maximumConcurrencyLevel = ThreadPoolConfig.ScaledPoolSize(
                        config.GetInt("parallelism-min"),
                        config.GetDouble("parallelism-factor", 1.0D), // the scalar-based factor to scale the threadpool size to 
                        config.GetInt("parallelism-max"));
            _maximumConcurrencyLevel = Math.Max(_maximumConcurrencyLevel, 1);
            _maxWork = Math.Max(config.GetInt("work-max", _maxWork), 3);

            _workInterval = config.GetInt("work-interval", _workInterval);
            _workStep = config.GetInt("work-step", _workStep);

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

            _coworkers = new Task[_maximumConcurrencyLevel - 1];
            for (var i = 0; i < _coworkers.Length; i++)
                _coworkers[i] = Task.CompletedTask;

            _timer = new Timer(ScheduleCoWorkers, "timer", Timeout.Infinite, Timeout.Infinite);

            _controlTask = Task.Factory.StartNew(ControlAsync, _cts.Token,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap();
        }

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

                //wait on coworker exit
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

                readTask = await Task.WhenAny(readTasks).ConfigureAwait(false);
            }
            while (readTask.Result && !_cts.IsCancellationRequested);
        }

        private void ScheduleCoWorkers(object state)
        {
            var name = (string)state;

            var queuedWorkItems = _highScheduler.Channel.Reader.Count
                + _normalScheduler.Channel.Reader.Count
                + _lowScheduler.Channel.Reader.Count
                + _idleScheduler.Channel.Reader.Count;

            var reqWorkerCount = queuedWorkItems;

            //limit req workers
            reqWorkerCount = Math.Min(reqWorkerCount, _maximumConcurrencyLevel);

            //count running workers
            var controlWorkerCount = name == "control" ? 1 : 0;
            var coworkerCount = 0;
            for (int i = 0; i < _coworkers.Length; i++)
            {
                if (!_coworkers[i].IsCompleted)
                    coworkerCount++;
            }

            //limit new workers
            var newWorkerToStart = Math.Min(Math.Max(reqWorkerCount - controlWorkerCount - coworkerCount, 0), _workStep);
            if (newWorkerToStart == 0 && reqWorkerCount > controlWorkerCount && (controlWorkerCount+coworkerCount) < _maximumConcurrencyLevel)
                newWorkerToStart = 1;

            if (newWorkerToStart > 0)
            {
                //start new workers
                for (var i = 0; newWorkerToStart > 0 && i < _coworkers.Length; i++)
                {
                    if (_coworkers[i].IsCompleted)
                    {
                        _coworkers[i] = Task.Factory.StartNew(Worker, i + 1, _cts.Token,
                            TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                        newWorkerToStart--;
                    }
                }
            }

            //reschedule
            if (!_cts.IsCancellationRequested)
            {
                var interval = controlWorkerCount > 0 || (reqWorkerCount - newWorkerToStart) > 0
                    ? _workInterval / _workStep
                    : _workInterval * _workStep;
                _timer.Change(interval, Timeout.Infinite);
            }
        }

        private void Worker(object state)
        {
            DoWork((int)state);
        }

        private int DoWork(int workerId)
        {
            var highCount = 0;
            var normalCount = 0;
            var lowCount = 0;
            var idleCount = 0;

            int c;
            int rounds = 0;
            int roundWork;
            int roundClean = 0;

            //maybe implement max work count and/or a deadline

            _threadPriority = TaskSchedulerPriority.Idle;
            try
            {
                do
                {
                    rounds++;
                    roundWork = 0;

                    c = _highScheduler.ExecuteAll();
                    highCount += c;
                    roundWork += c;

                    c = _normalScheduler.ExecuteMany(_maxWork);
                    normalCount += c;
                    roundWork += c;

                    c = roundWork > 0
                        ? _lowScheduler.ExecuteSingle()
                        : _lowScheduler.ExecuteMany(_maxWork);
                    lowCount += c;
                    roundWork += c;

                    //if there was no work then only execute background tasks 
                    if (c == 0)
                    {
                        c = _idleScheduler.ExecuteSingle();
                        idleCount += c;
                        roundWork += c;
                    }

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

        sealed class PriorityTaskScheduler : TaskScheduler, IDisposable
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
                // and the thread priority is higher,
                // we don't support inlining
                return (_threadPriority > TaskSchedulerPriority.None && _threadPriority <= _priority) 
                    && TryExecuteTask(task);
            }

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
