//-----------------------------------------------------------------------
// <copyright file="Cancelable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;

namespace Akka.Actor
{
    /// <summary>
    /// A <see cref="ICancelable"/> that wraps a <see cref="CancellationTokenSource"/>. 
    /// When canceling this instance the underlying <see cref="CancellationTokenSource"/> is canceled as well.
    /// </summary>
    public sealed class Cancelable : ICancelable, IDisposable
    {
        private readonly IActionScheduler _scheduler;
        private readonly CancellationTokenSource _source;
        private long _deadline = long.MaxValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="Cancelable"/> class that will be cancelled after 
        /// the specified amount of time.
        /// </summary>
        /// <param name="scheduler">The scheduler.</param>
        /// <param name="delay">The delay before the cancelable is canceled.</param>
        public Cancelable(IActionScheduler scheduler, TimeSpan delay)
            : this(scheduler)
        {
            CancelAfter(delay);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cancelable"/> class that will be cancelled after 
        /// the specified amount of time.
        /// </summary>
        /// <param name="scheduler">The scheduler.</param>
        /// <param name="delay">The delay before the cancelable is canceled.</param>
        public Cancelable(IScheduler scheduler, TimeSpan delay)
            : this(scheduler.Advanced)
        {
            CancelAfter(delay);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cancelable"/> class that will be cancelled after 
        /// the specified amount of milliseconds.
        /// </summary>
        /// <param name="scheduler">The scheduler.</param>
        /// <param name="millisecondsDelay">The delay in milliseconds.</param>
        public Cancelable(IScheduler scheduler, int millisecondsDelay)
            : this(scheduler.Advanced)
        {
            CancelAfter(millisecondsDelay);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cancelable"/> class.
        /// </summary>
        /// <param name="scheduler">TBD</param>
        public Cancelable(IScheduler scheduler)
            : this(scheduler.Advanced)
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cancelable"/> class.
        /// </summary>
        /// <param name="scheduler">TBD</param>
        public Cancelable(IActionScheduler scheduler)
            : this(scheduler, new CancellationTokenSource())
        {
            //Intentionally left blank
        }

        [Obsolete("Only to be used from DeprecatedSchedulerExtensions. [1.0.0]")] //TODO: Remove this line and make it private when DeprecatedSchedulerExtensions is removed
        internal Cancelable(IActionScheduler scheduler, CancellationTokenSource source)
        {
            _source = source;
            _scheduler = scheduler;
        }


        /// <inheritdoc/>
        public bool IsCancellationRequested => _source.IsCancellationRequested;

        /// <inheritdoc/>
        public CancellationToken Token => _source.Token;

        /// <summary>
        /// Deadline of cancellation in scheduler time
        /// </summary>
        public long Deadline => _deadline;

        /// <inheritdoc/>
        public void Cancel()
        {
            Cancel(false);
        }

        /// <inheritdoc/>
        public void Cancel(bool throwOnFirstException)
        {
            _source.Cancel(throwOnFirstException);
        }

        /// <inheritdoc/>
        public void CancelAfter(TimeSpan delay)
        {
            if (delay < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(delay), $"The delay must be >0, it was {delay}");
            InternalCancelAfter(delay);
        }

        /// <inheritdoc/>
        public void CancelAfter(int millisecondsDelay)
        {
            if (millisecondsDelay < 0)
                throw new ArgumentOutOfRangeException(nameof(millisecondsDelay), $"The delay must be >0, it was {millisecondsDelay}");
            InternalCancelAfter(TimeSpan.FromMilliseconds(millisecondsDelay));
        }

        private void InternalCancelAfter(TimeSpan delay)
        {
            if (_source.IsCancellationRequested)
                return;

            //If the scheduler is using the system time, we can optimize for that
            if (_scheduler is IDateTimeOffsetNowTimeProvider time)
            {
                //Use the built in functionality on CancellationTokenSource which is
                //likely more lightweight than using the scheduler
                _source.CancelAfter(delay);
                _deadline = time.HighResMonotonicClock.Ticks + delay.Ticks;
            }
            else
            {
                _scheduler.ScheduleOnce(delay, () => _source.Cancel(), this);
            }
        }

        /// <summary>
        /// Returns a <see cref="ICancelable"/> that has already been canceled.
        /// </summary>
        public static ICancelable CreateCanceled()
        {
            return AlreadyCanceledCancelable.Instance;
        }

        /// <summary>
        /// Creates a <see cref="ICancelable"/> that will be in the canceled state
        /// when any of the source cancelables are in the canceled state. 
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="cancelables">The cancelables instances to observe.</param>
        /// <returns>A new <see cref="ICancelable"/> that is linked to the source .</returns>
        public static ICancelable CreateLinkedCancelable(IScheduler scheduler, params ICancelable[] cancelables)
        {
            var cancellationTokens = cancelables.Select(c => c.Token).ToArray();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens);
            return new Cancelable(scheduler.Advanced, cts);
        }

        /// <summary>
        /// Creates a <see cref="ICancelable"/> that will be in the canceled state
        /// when any of the source cancelables are in the canceled state. 
        /// </summary>
        /// <param name="scheduler">The scheduler</param>
        /// <param name="cancelables">The cancelables instances to observe.</param>
        /// <returns>A new <see cref="ICancelable"/> that is linked to the source .</returns>
        public static ICancelable CreateLinkedCancelable(IActionScheduler scheduler, params ICancelable[] cancelables)
        {
            var cancellationTokens = cancelables.Select(c => c.Token).ToArray();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens);
            return new Cancelable(scheduler, cts);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _source?.Dispose();
        }
    }
}

