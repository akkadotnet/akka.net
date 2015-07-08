/*
 * Copyright 2015 Roger Alsing, Aaron Stannard
 * Helios.DedicatedThreadPool - https://github.com/helios-io/DedicatedThreadPool
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Helios.Concurrency
{
    /// <summary>
    /// The type of threads to use - either foreground or background threads.
    /// </summary>
    internal enum ThreadType
    {
        Foreground,
        Background
    }

    /// <summary>
    /// Provides settings for a dedicated thread pool
    /// </summary>
    internal class DedicatedThreadPoolSettings
    {
        /// <summary>
        /// Background threads are the default thread type
        /// </summary>
        public const ThreadType DefaultThreadType = ThreadType.Background;

        public DedicatedThreadPoolSettings(int numThreads, string name = null, TimeSpan? deadlockTimeout = null, ApartmentState apartmentState = ApartmentState.Unknown) 
            : this(numThreads, DefaultThreadType, name, deadlockTimeout, apartmentState) { }

        public DedicatedThreadPoolSettings(int numThreads, ThreadType threadType, string name = null, TimeSpan? deadlockTimeout = null, ApartmentState apartmentState = ApartmentState.Unknown)
        {
            Name = name ?? ("DedicatedThreadPool-" + Guid.NewGuid());
            ThreadType = threadType;
            NumThreads = numThreads;
            DeadlockTimeout = deadlockTimeout;
            ApartmentState = apartmentState;
            if (deadlockTimeout.HasValue && deadlockTimeout.Value.TotalMilliseconds <= 0)
                throw new ArgumentOutOfRangeException("deadlockTimeout", string.Format("deadlockTimeout must be null or at least 1ms. Was {0}.", deadlockTimeout));
            if (numThreads <= 0)
                throw new ArgumentOutOfRangeException("numThreads", string.Format("numThreads must be at least 1. Was {0}", numThreads));
        }

        /// <summary>
        /// The total number of threads to run in this thread pool.
        /// </summary>
        public int NumThreads { get; private set; }

        /// <summary>
        /// The type of threads to run in this thread pool.
        /// </summary>
        public ThreadType ThreadType { get; private set; }

        /// <summary>
        /// Apartment state for threads to run in this thread pool
        /// </summary>
        public ApartmentState ApartmentState { get; private set; }

        /// <summary>
        /// Interval to check for thread deadlocks.
        /// 
        /// If a thread takes longer than <see cref="DeadlockTimeout"/> it will be aborted
        /// and replaced.
        /// </summary>
        public TimeSpan? DeadlockTimeout { get; private set; }

        public string Name { get; private set; }
    }

    /// <summary>
    /// TaskScheduler for working with a <see cref="DedicatedThreadPool"/> instance
    /// </summary>
    internal class DedicatedThreadPoolTaskScheduler : TaskScheduler
    {
        // Indicates whether the current thread is processing work items.
        [ThreadStatic]
        private static bool _currentThreadIsRunningTasks;

        /// <summary>
        /// Number of tasks currently running
        /// </summary>
        private volatile int _parallelWorkers = 0;

        private readonly LinkedList<Task> _tasks = new LinkedList<Task>();

        private readonly DedicatedThreadPool _pool;

        public DedicatedThreadPoolTaskScheduler(DedicatedThreadPool pool)
        {
            _pool = pool;
        }

        protected override void QueueTask(Task task)
        {
            lock (_tasks)
            {
                _tasks.AddLast(task);
            }

            EnsureWorkerRequested();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            //current thread isn't running any tasks, can't execute inline
            if (!_currentThreadIsRunningTasks) return false;

            //remove the task from the queue if it was previously added
            if (taskWasPreviouslyQueued)
                if (TryDequeue(task))
                    return TryExecuteTask(task);
                else
                    return false;
            return TryExecuteTask(task);
        }

        protected override bool TryDequeue(Task task)
        {
            lock (_tasks) return _tasks.Remove(task);
        }

        /// <summary>
        /// Level of concurrency is directly equal to the number of threads
        /// in the <see cref="DedicatedThreadPool"/>.
        /// </summary>
        public override int MaximumConcurrencyLevel
        {
            get { return _pool.Settings.NumThreads; }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(_tasks, ref lockTaken);

                //should this be immutable?
                if (lockTaken) return _tasks;
                else throw new NotSupportedException();
            }
            finally
            {
                if (lockTaken) Monitor.Exit(_tasks);
            }
        }

        private void EnsureWorkerRequested()
        {
            var count = _parallelWorkers;
            while (count < _pool.Settings.NumThreads)
            {
                var prev = Interlocked.CompareExchange(ref _parallelWorkers, count + 1, count);
                if (prev == count)
                {
                    RequestWorker();
                    break;
                }
                count = prev;
            }
        }

        private void ReleaseWorker()
        {
            var count = _parallelWorkers;
            while (count > 0)
            {
                var prev = Interlocked.CompareExchange(ref _parallelWorkers, count - 1, count);
                if (prev == count)
                {
                    break;
                }
                count = prev;
            }
        }

        private void RequestWorker()
        {
            _pool.QueueUserWorkItem(() =>
            {
                // this thread is now available for inlining
                _currentThreadIsRunningTasks = true;
                try
                {
                    // Process all available items in the queue. 
                    while (true)
                    {
                        Task item;
                        lock (_tasks)
                        {
                            // done processing
                            if (_tasks.Count == 0)
                            {
                                ReleaseWorker();
                                break;
                            }

                            // Get the next item from the queue
                            item = _tasks.First.Value;
                            _tasks.RemoveFirst();
                        }

                        // Execute the task we pulled out of the queue 
                        TryExecuteTask(item);
                    }
                }
                // We're done processing items on the current thread 
                finally { _currentThreadIsRunningTasks = false; }
            });
        }
    }



    /// <summary>
    /// An instanced, dedicated thread pool.
    /// </summary>
    internal class DedicatedThreadPool : IDisposable
    {
        internal class DedicatedThreadPoolSupervisor : IDisposable
        {
            private readonly Timer _timer;

            internal DedicatedThreadPoolSupervisor(DedicatedThreadPool pool)
            {

                //don't set up a timer if a timeout wasn't specified
                if (pool.Settings.DeadlockTimeout == null)
                    return;

                _timer = new Timer(_ =>
                {
                    //bail in the event of a shutdown
                    if (pool.ShutdownRequested) return;

                    for (var i = 0; i < pool.Workers.Length; i++)
                    {
                        var w = pool.Workers[i];
                        if (Interlocked.Exchange(ref w.Status, 0) == 0)
                        {
                            //this requests a new new worker and calls ForceTermination on the old worker
                            //Potential problem here: if the thread is not dead for real, we might abort real work.. there is no way to tell the difference between 
                            //deadlocked or just very long running tasks
                            var newWorker = pool.RequestThread(w, i);
                            continue;
                        }

                        //schedule heartbeat action to worker
                        pool.Workers[i].AddWork(() => Interlocked.Increment(ref w.Status));
                    }
                }, null, pool.Settings.DeadlockTimeout.Value, pool.Settings.DeadlockTimeout.Value);
            }

            public void Dispose()
            {
                /*
                 * Timer can be null if no deadlock interval was defined in
                 * DedicatedThreadPoolSettings.
                 */
                if (_timer != null)
                {
                    _timer.Dispose();
                }
            }
        }

        public DedicatedThreadPool(DedicatedThreadPoolSettings settings)
        {
            Settings = settings;

            Workers = Enumerable.Repeat(0, settings.NumThreads).Select(_ => new WorkerQueue()).ToArray();
            for (var i = 0; i < Workers.Length; i++)
            {
                new PoolWorker(Workers[i], this, false, i);
            }
            _supervisor = new DedicatedThreadPoolSupervisor(this);
        }

        public DedicatedThreadPoolSettings Settings { get; private set; }

        internal volatile bool ShutdownRequested;

        public readonly WorkerQueue[] Workers;

        [ThreadStatic]
        internal static PoolWorker CurrentWorker;

        /// <summary>
        /// index for round-robin load-balancing across worker threads
        /// </summary>
        private volatile int _index;

        private readonly DedicatedThreadPoolSupervisor _supervisor;

        public bool WasDisposed { get; private set; }

        private void Shutdown()
        {
            ShutdownRequested = true;
        }

        private PoolWorker RequestThread(WorkerQueue unclaimedQueue, int workerNumber, bool errorRecovery = false)
        {
            var worker = new PoolWorker(unclaimedQueue, this, errorRecovery, workerNumber);
            return worker;
        }

        public bool QueueUserWorkItem(Action work)
        {
            bool success = true;

            //don't queue work if we've been disposed
            if (WasDisposed) return false;

            if (work != null)
            {
                //no local queue, write to a round-robin queue
                //if (null == CurrentWorker)
                //{
                //using volatile instead of interlocked, no need to be exact, gaining 20% perf
                unchecked
                {
                    _index = (_index + 1);
                    //need to wrap bitwise operations in parens to preserve order, otherwise this won't round-robin
                    //to some actors if Settings.NumThreads is an odd number
                    Workers[(_index & 0x7fffffff) % Settings.NumThreads].AddWork(work);
                }
                //}
                //else //recursive task queue, write directly
                //{
                //    // send work directly to PoolWorker
                //    // CurrentWorker.AddWork(work);
                //}
            }
            else
            {
                throw new ArgumentNullException("work");
            }
            return success;
        }

        #region IDisposable members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool isDisposing)
        {
            if (!WasDisposed)
            {
                if (isDisposing)
                {
                    _supervisor.Dispose();
                    Shutdown();
                }
            }

            WasDisposed = true;
        }

        #endregion

        #region Pool worker implementation

        internal sealed class WorkerQueue
        {
            internal BlockingCollection<Action> WorkQueue = new BlockingCollection<Action>();
            internal int Status = 1;
            private PoolWorker _poolWorker;

            public void AddWork(Action work)
            {
                WorkQueue.Add(work);
            }

            internal void ReplacePoolWorker(PoolWorker poolWorker, bool errorRecovery)
            {
                if (_poolWorker != null && !errorRecovery)
                {
                    _poolWorker.ForceTermination();
                }
                _poolWorker = poolWorker;
            }
        }

        internal class PoolWorker
        {
            private WorkerQueue _work;
            private DedicatedThreadPool _pool;
            private readonly int _workerNumber;

            private BlockingCollection<Action> _workQueue;
            private readonly Thread _thread;

            public PoolWorker(WorkerQueue work, DedicatedThreadPool pool, bool errorRecovery, int workerNumber)
            {
                _work = work;
                _pool = pool;
                _workerNumber = workerNumber;
                _workQueue = _work.WorkQueue;
                _work.ReplacePoolWorker(this, errorRecovery);
                
                _thread = new Thread(() =>
                {
                    Thread.CurrentThread.Name = string.Format("{0}_{1}", pool.Settings.Name, _workerNumber);
                    CurrentWorker = this;

                    foreach (var action in _workQueue.GetConsumingEnumerable())
                    {
                        try
                        {
                            //bail if shutdown has been requested
                            if (_pool.ShutdownRequested) return;
                            action();
                        }
                        catch (Exception)
                        {
                            Failover(true);
                            return;
                        }
                    }
                })
                {
                    IsBackground = _pool.Settings.ThreadType == ThreadType.Background
                };
                if (_pool.Settings.ApartmentState != ApartmentState.Unknown)
                    _thread.SetApartmentState(_pool.Settings.ApartmentState);

                _thread.Start();
            }

            private void Failover(bool errorRecovery = false)
            {
                /* request a new thread then shut down */
                _pool.RequestThread(_work, _workerNumber, errorRecovery);
                CurrentWorker = null;
                _work = null;
                _workQueue = null;
                _pool = null;
            }

            internal void ForceTermination()
            {
                //TODO: abort is no guarantee for thread abortion
                _thread.Abort();
            }
        }

        #endregion
    }
}

