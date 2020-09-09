//-----------------------------------------------------------------------
// <copyright file="HashedWheelTimerScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util;

// ReSharper disable NotResolvedInText

namespace Akka.Actor
{
    /// <summary>
    /// This <see cref="IScheduler"/> implementation is built using a revolving wheel of buckets
    /// with each bucket belonging to a specific time resolution. As the "clock" of the scheduler ticks it advances
    /// to the next bucket in the circle and processes the items in it, and optionally reschedules recurring
    /// tasks into the future into the next relevant bucket.
    ///
    /// There are `akka.scheduler.ticks-per-wheel` initial buckets (we round up to the nearest power of 2) with 512
    /// being the initial default value. The timings are approximated and are still limited by the ceiling of the operating
    /// system's clock resolution.
    ///
    /// Further reading: http://www.cs.columbia.edu/~nahum/w6998/papers/sosp87-timing-wheels.pdf
    /// Presentation: http://www.cse.wustl.edu/~cdgill/courses/cs6874/TimingWheels.ppt
    /// </summary>
    public class HashedWheelTimerScheduler : SchedulerBase, IDateTimeOffsetNowTimeProvider, IDisposable
    {
        private readonly TimeSpan _shutdownTimeout;
        private readonly long _tickDuration; // a timespan expressed as ticks

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="scheduler">TBD</param>
        /// <param name="log">TBD</param>
        /// <exception cref="ArgumentOutOfRangeException">TBD</exception>
        public HashedWheelTimerScheduler(Config scheduler, ILoggingAdapter log) : base(scheduler, log)
        {
            if (SchedulerConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<HashedWheelTimerScheduler>();

            var ticksPerWheel = SchedulerConfig.GetInt("akka.scheduler.ticks-per-wheel", 0);
            var tickDuration = SchedulerConfig.GetTimeSpan("akka.scheduler.tick-duration", null);
            if (tickDuration.TotalMilliseconds < 10.0d)
                throw new ArgumentOutOfRangeException("minimum supported akka.scheduler.tick-duration on Windows is 10ms");

            // convert tick-duration to ticks
            _tickDuration = tickDuration.Ticks;

            // Normalize ticks per wheel to power of two and create the wheel
            _wheel = CreateWheel(ticksPerWheel, log);
            _mask = _wheel.Length - 1;

            // prevent overflow
            if (_tickDuration >= long.MaxValue / _wheel.Length)
                throw new ArgumentOutOfRangeException("akka.scheduler.tick-duration", _tickDuration,
                    $"akka.scheduler.tick-duration: {_tickDuration} (expected: 0 < tick-duration in ticks < {long.MaxValue / _wheel.Length}");

            _shutdownTimeout = SchedulerConfig.GetTimeSpan("akka.scheduler.shutdown-timeout", null);
        }

        private long _startTime = 0;
        private long _tick;
        private readonly int _mask;
        private readonly CountdownEvent _workerInitialized = new CountdownEvent(1);
        private readonly ConcurrentQueue<SchedulerRegistration> _registrations = new ConcurrentQueue<SchedulerRegistration>();
        private readonly Bucket[] _wheel;

        private const int WORKER_STATE_INIT = 0;
        private const int WORKER_STATE_STARTED = 1;
        private const int WORKER_STATE_SHUTDOWN = 2;

        /// <summary>
        /// 0 - init, 1 - started, 2 - shutdown
        /// </summary>
        private volatile int _workerState = WORKER_STATE_INIT;

        private static Bucket[] CreateWheel(int ticksPerWheel, ILoggingAdapter log)
        {
            if (ticksPerWheel <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerWheel), ticksPerWheel, "Must be greater than 0.");
            if (ticksPerWheel > 1073741824)
                throw new ArgumentOutOfRangeException(nameof(ticksPerWheel), ticksPerWheel, "Cannot be greater than 2^30.");

            ticksPerWheel = NormalizeTicksPerWheel(ticksPerWheel);
            var wheel = new Bucket[ticksPerWheel];
            for (var i = 0; i < wheel.Length; i++)
                wheel[i] = new Bucket(log);
            return wheel;
        }

        /// <summary>
        /// Normalize a wheel size to the nearest power of 2.
        /// </summary>
        /// <param name="ticksPerWheel">The original input per wheel.</param>
        /// <returns><paramref name="ticksPerWheel"/> normalized to the nearest power of 2.</returns>
        private static int NormalizeTicksPerWheel(int ticksPerWheel)
        {
            var normalizedTicksPerWheel = 1;
            while (normalizedTicksPerWheel < ticksPerWheel)
                normalizedTicksPerWheel <<= 1;
            return normalizedTicksPerWheel;
        }

        private readonly HashSet<SchedulerRegistration> _unprocessedRegistrations = new HashSet<SchedulerRegistration>();
        private readonly HashSet<SchedulerRegistration> _rescheduleRegistrations = new HashSet<SchedulerRegistration>();

        private Thread _worker;

        private void Start()
        {
            if (_workerState == WORKER_STATE_STARTED) { } // do nothing
            else if (_workerState == WORKER_STATE_INIT)
            {
                _worker = new Thread(Run) { IsBackground = true };
#pragma warning disable 420
                if (Interlocked.CompareExchange(ref _workerState, WORKER_STATE_STARTED, WORKER_STATE_INIT) ==
#pragma warning restore 420
                    WORKER_STATE_INIT)
                {
                    _worker.Start();
                }
            }

            else if (_workerState == WORKER_STATE_SHUTDOWN)
            {
                throw new SchedulerException("cannot enqueue after timer shutdown");
            }
            else
            {
                throw new InvalidOperationException($"Worker in invalid state: {_workerState}");
            }

            while (_startTime == 0)
            {
                _workerInitialized.Wait();
            }
        }

        /// <summary>
        /// Scheduler thread entry method
        /// </summary>
        private void Run()
        {
            // Initialize the clock
            _startTime = HighResMonotonicClock.Ticks;
            if (_startTime == 0)
            {
                // 0 means it's an uninitialized value, so bump to 1 to indicate it's started
                _startTime = 1;
            }

            _workerInitialized.Signal();

            do
            {
                var deadline = WaitForNextTick();
                if (deadline > 0)
                {
                    var idx = (int)(_tick & _mask);
                    var bucket = _wheel[idx];
                    TransferRegistrationsToBuckets();
                    bucket.Execute(deadline);
                    _tick++; // it will take 2^64 * 10ms for this to overflow

                    bucket.ClearReschedule(_rescheduleRegistrations);
                    ProcessReschedule();
                }
            } while (_workerState == WORKER_STATE_STARTED);

            // empty all of the buckets
            foreach (var bucket in _wheel)
                bucket.ClearRegistrations(_unprocessedRegistrations);

            // empty tasks that haven't been placed into a bucket yet
            foreach (var reg in _registrations)
            {
                if (!reg.Cancelled)
                    _unprocessedRegistrations.Add(reg);
            }

            // return the list of unprocessedRegistrations and signal that we're finished
            _stopped.Value.TrySetResult(_unprocessedRegistrations);
        }

        private void ProcessReschedule()
        {
            foreach (var sched in _rescheduleRegistrations)
            {
                var nextDeadline = HighResMonotonicClock.Ticks - _startTime + sched.Offset;
                sched.Deadline = nextDeadline;
                PlaceInBucket(sched);
            }
            _rescheduleRegistrations.Clear();
        }

        private long WaitForNextTick()
        {
            var deadline = _tickDuration * (_tick + 1);
            unchecked // just to avoid trouble with long-running applications
            {
                for (;;)
                {
                    long currentTime = HighResMonotonicClock.Ticks - _startTime;
                    var sleepMs = ((deadline - currentTime + TimeSpan.TicksPerMillisecond - 1) / TimeSpan.TicksPerMillisecond);

                    if (sleepMs <= 0) // no need to sleep
                    {
                        if (currentTime == long.MinValue) // wrap-around
                            return -long.MaxValue;
                        return currentTime;

                    }

                    Thread.Sleep(TimeSpan.FromMilliseconds(sleepMs));
                }
            }
        }

        private void TransferRegistrationsToBuckets()
        {
            // transfer only max. 100000 registrations per tick to prevent a thread to stale the workerThread when it just
            // adds new timeouts in a loop.
            for (var i = 0; i < 100000; i++)
            {
                SchedulerRegistration reg;
                if (!_registrations.TryDequeue(out reg))
                {
                    // all processed
                    break;
                }

                if (reg.Cancelled)
                {
                    // cancelled before we could process it
                    continue;
                }

                PlaceInBucket(reg);
            }
        }

        private void PlaceInBucket(SchedulerRegistration reg)
        {
            var calculated = reg.Deadline / _tickDuration;
            reg.RemainingRounds = (calculated - _tick) / _wheel.Length;

            var ticks = Math.Max(calculated, _tick); // Ensure we don't schedule for the past
            var stopIndex = (int)(ticks & _mask);

            var bucket = _wheel[stopIndex];
            bucket.AddRegistration(reg);
        }


        /// <summary>
        /// TBD
        /// </summary>
        protected override DateTimeOffset TimeNow => DateTimeOffset.Now;
        /// <summary>
        /// TBD
        /// </summary>
        public override TimeSpan MonotonicClock => Util.MonotonicClock.Elapsed;

        /// <summary>
        /// TBD
        /// </summary>
        public override TimeSpan HighResMonotonicClock => Util.MonotonicClock.ElapsedHighRes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="receiver">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected override void InternalScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender,
                    ICancelable cancelable)
        {
            InternalSchedule(delay, TimeSpan.Zero, new ScheduledTell(receiver, message, sender), cancelable);
        }

        private void InternalSchedule(TimeSpan delay, TimeSpan interval, IRunnable action, ICancelable cancelable)
        {
            Start();
            var deadline = HighResMonotonicClock.Ticks + delay.Ticks - _startTime;
            var offset = interval.Ticks;
            var reg = new SchedulerRegistration(action, cancelable)
            {
                Deadline = deadline,
                Offset = offset
            };
            _registrations.Enqueue(reg);
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
        protected override void InternalScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message,
                    IActorRef sender, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, new ScheduledTell(receiver, message, sender), cancelable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected override void InternalScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable)
        {
            InternalSchedule(delay, TimeSpan.Zero, new ActionRunnable(action), cancelable);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <param name="cancelable">TBD</param>
        protected override void InternalScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable)
        {
            InternalSchedule(initialDelay, interval, new ActionRunnable(action), cancelable);
        }

        private AtomicReference<TaskCompletionSource<IEnumerable<SchedulerRegistration>>> _stopped = new AtomicReference<TaskCompletionSource<IEnumerable<SchedulerRegistration>>>();

        private static readonly Task<IEnumerable<SchedulerRegistration>> Completed = Task.FromResult((IEnumerable<SchedulerRegistration>)new List<SchedulerRegistration>());

        private Task<IEnumerable<SchedulerRegistration>> Stop()
        {
            var p = new TaskCompletionSource<IEnumerable<SchedulerRegistration>>();

            if (_stopped.CompareAndSet(null, p)
#pragma warning disable 420
                && Interlocked.CompareExchange(ref _workerState, WORKER_STATE_SHUTDOWN, WORKER_STATE_STARTED) == WORKER_STATE_STARTED)
#pragma warning restore 420
            {
                // Let remaining work that is already being processed finished. The termination task will complete afterwards
                return p.Task;
            }
            return Completed;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            var stopped = Stop();
            if (!stopped.Wait(_shutdownTimeout))
            {
                Log.Warning("Failed to shutdown scheduler within {0}", _shutdownTimeout);
                return;
            }

            // Execute all outstanding work items
            foreach (var task in stopped.Result)
            {
                try
                {
                    task.Action.Run();
                }
                catch (SchedulerException)
                {
                    // ignore, this is from terminated actors
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Exception while executing timer task.");
                }
                finally
                {
                    // free the object from bucket
                    task.Reset();
                }
            }

            _unprocessedRegistrations.Clear();
        }

        /// <summary>
        /// INTERNAL API.
        /// </summary>
        private sealed class ScheduledTell : IRunnable
        {
            private readonly ICanTell _receiver;
            private readonly object _message;
            private readonly IActorRef _sender;

            public ScheduledTell(ICanTell receiver, object message, IActorRef sender)
            {
                _receiver = receiver;
                _message = message;
                _sender = sender;
            }

            public void Run()
            {
                _receiver.Tell(_message, _sender);
            }

            public override string ToString()
            {
                return $"[{_receiver}.Tell({_message}, {_sender})]";
            }
        }

        private class SchedulerRegistration
        {
            /// <summary>
            /// The cancellation handle, if any
            /// </summary>
            public readonly ICancelable Cancellation;

            /// <summary>
            /// The task to be executed
            /// </summary>
            public readonly IRunnable Action;

            /*
             * Linked list is only ever modified from the scheduler thread, so this
             * implementation does not need to be synchronized or implement CAS semantics.
             */
            public SchedulerRegistration Next;
            public SchedulerRegistration Prev;

            public long RemainingRounds;

            /// <summary>
            /// Used to determine the delay for repeatable sends
            /// </summary>
            public long Offset;

            /// <summary>
            /// The deadline for determining when to execute
            /// </summary>
            public long Deadline;

            public SchedulerRegistration(IRunnable action, ICancelable cancellation)
            {
                Action = action;
                Cancellation = cancellation;
            }

            /// <summary>
            /// Determines if this task will need to be re-scheduled according to its <see cref="Offset"/>.
            /// </summary>
            public bool Repeat => Offset > 0;

            /// <summary>
            /// If <c>true</c>, we skip execution of this task.
            /// </summary>
            public bool Cancelled => Cancellation?.IsCancellationRequested ?? false;

            /// <summary>
            /// The <see cref="Bucket"/> to which this registration belongs.
            /// </summary>
            public Bucket Bucket;

            /// <summary>
            /// Resets all of the fields so this registration object can be used again
            /// </summary>
            public void Reset()
            {
                Next = null;
                Prev = null;
                Bucket = null;
                Deadline = 0;
                RemainingRounds = 0;
            }

            public override string ToString()
            {
                return
                    $"ScheduledWork(Deadline={Deadline}, RepeatEvery={Offset}, Cancelled={Cancelled}, Work={Action})";
            }
        }

        private sealed class Bucket
        {
            private readonly ILoggingAdapter _log;

            /*
             * Endpoints of our doubly linked list
             */
            private SchedulerRegistration _head;
            private SchedulerRegistration _tail;

            private SchedulerRegistration _rescheduleHead;
            private SchedulerRegistration _rescheduleTail;

            public Bucket(ILoggingAdapter log)
            {
                _log = log;
            }

            /// <summary>
            /// Adds a <see cref="SchedulerRegistration"/> to this bucket.
            /// </summary>
            /// <param name="reg">The scheduled task.</param>
            public void AddRegistration(SchedulerRegistration reg)
            {
                System.Diagnostics.Debug.Assert(reg.Bucket == null);
                reg.Bucket = this;
                if (_head == null) // first time the bucket has been used
                {
                    _head = _tail = reg;
                }
                else
                {
                    _tail.Next = reg;
                    reg.Prev = _tail;
                    _tail = reg;
                }
            }

            /// <summary>
            /// Slot a repeating task into the "reschedule" linked list.
            /// </summary>
            /// <param name="reg">The registration scheduled for repeating</param>
            public void Reschedule(SchedulerRegistration reg)
            {
                if (_rescheduleHead == null)
                {
                    _rescheduleHead = _rescheduleTail = reg;
                }
                else
                {
                    _rescheduleTail.Next = reg;
                    reg.Prev = _rescheduleTail;
                    _rescheduleTail = reg;
                }
            }

            /// <summary>
            /// Empty this bucket
            /// </summary>
            /// <param name="registrations">A set of registrations to populate.</param>
            public void ClearRegistrations(HashSet<SchedulerRegistration> registrations)
            {
                for (;;)
                {
                    var reg = Poll();
                    if (reg == null)
                        return;
                    if (reg.Cancelled)
                        continue;
                    registrations.Add(reg);
                }
            }

            /// <summary>
            /// Reset the reschedule list for this bucket
            /// </summary>
            /// <param name="registrations">A set of registrations to populate.</param>
            public void ClearReschedule(HashSet<SchedulerRegistration> registrations)
            {
                for (;;)
                {
                    var reg = PollReschedule();
                    if (reg == null)
                        return;
                    if (reg.Cancelled)
                        continue;
                    registrations.Add(reg);
                }
            }

            private static readonly Action<object> ExecuteRunnableWithState = r => ((IRunnable)r).Run();

            /// <summary>
            /// Execute all <see cref="SchedulerRegistration"/>s that are due by or after <paramref name="deadline"/>.
            /// </summary>
            /// <param name="deadline">The execution time.</param>
            public void Execute(long deadline)
            {
                var current = _head;

                // process all registrations
                while (current != null)
                {
                    bool remove = false;
                    if (current.Cancelled) // check for cancellation first
                    {
                        remove = true;
                    }
                    else if (current.RemainingRounds <= 0)
                    {
                        if (current.Deadline <= deadline)
                        {
                            try
                            {
                                // Execute the scheduled work
                                current.Action.Run();
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    _log.Error(ex, "Error while executing scheduled task {0}", current);
                                    var nextErrored = current.Next;
                                    Remove(current);
                                    current = nextErrored;
                                    continue; // don't reschedule any failed actions
                                }
                                catch
                                {
                                } // suppress any errors thrown during logging
                            }
                            remove = true;
                        }
                        else
                        {
                            // Registration was placed into the wrong bucket. This should never happen.
                            throw new InvalidOperationException(
                                $"SchedulerRegistration.Deadline [{current.Deadline}] > Timer.Deadline [{deadline}]");
                        }
                    }
                    else
                    {
                        current.RemainingRounds--;
                    }

                    var next = current.Next;
                    if (remove)
                    {
                        Remove(current);
                    }
                    if (current.Repeat && remove)
                    {
                        Reschedule(current);
                    }
                    current = next;
                }
            }

            public void Remove(SchedulerRegistration reg)
            {
                var next = reg.Next;

                // Remove work that's already been completed or cancelled
                // Work that is scheduled to repeat will be handled separately
                if (reg.Prev != null)
                {
                    reg.Prev.Next = next;
                }
                if (reg.Next != null)
                {
                    reg.Next.Prev = reg.Prev;
                }

                if (reg == _head)
                {
                    // need to adjust the ends
                    if (reg == _tail)
                    {
                        _tail = null;
                        _head = null;
                    }
                    else
                    {
                        _head = next;
                    }
                }
                else if (reg == _tail)
                {
                    _tail = reg.Prev;
                }

                // detach the node from Linked list so it can be GCed
                reg.Reset();
            }

            private SchedulerRegistration Poll()
            {
                var head = _head;
                if (head == null)
                {
                    return null;
                }
                var next = head.Next;
                if (next == null)
                {
                    _tail = _head = null;
                }
                else
                {
                    _head = next;
                    next.Prev = null;
                }

                head.Reset();
                return head;
            }

            private SchedulerRegistration PollReschedule()
            {
                var head = _rescheduleHead;
                if (head == null)
                {
                    return null;
                }
                var next = head.Next;
                if (next == null)
                {
                    _rescheduleTail = _rescheduleHead = null;
                }
                else
                {
                    _rescheduleHead = next;
                    next.Prev = null;
                }

                head.Reset();
                return head;
            }
        }
    }
}

