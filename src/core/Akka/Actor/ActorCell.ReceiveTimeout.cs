//-----------------------------------------------------------------------
// <copyright file="ActorCell.ReceiveTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// Marker interface to indicate that a message should not reset the receive timeout.
    /// </summary>
    public interface INotInfluenceReceiveTimeout
    {
    }

    public partial class ActorCell
    {
        private TimeSpan? _receiveTimeoutDuration;
        private ICancelable _pendingReceiveTimeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public void SetReceiveTimeout(TimeSpan? timeout = null)
        {
            _receiveTimeoutDuration = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? ReceiveTimeout => _receiveTimeoutDuration;

        /// <summary>
        /// TBD
        /// </summary>
        public void CheckReceiveTimeout(bool reschedule = true)
        {
            if (_receiveTimeoutDuration != null)
            {
                // The fact that timeout is FiniteDuration and task is emptyCancellable
                // means that a user called `context.setReceiveTimeout(...)`
                // while sending the ReceiveTimeout message is not scheduled yet.
                // We have to handle the case and schedule sending the ReceiveTimeout message
                // ignoring the reschedule parameter.
                if (reschedule || _pendingReceiveTimeout == null)
                {
                    RescheduleReceiveTimeout(_receiveTimeoutDuration.Value);
                }
            }
            else
            {
                CancelReceiveTimeout();
            }
        }

        private void RescheduleReceiveTimeout(TimeSpan timeout)
        {
            _pendingReceiveTimeout.CancelIfNotNull(); //Cancel any ongoing future
            _pendingReceiveTimeout = System.Scheduler.ScheduleTellOnceCancelable(timeout, Self, Akka.Actor.ReceiveTimeout.Instance, Self);
            _receiveTimeoutDuration = timeout;
        }

        private void CancelReceiveTimeout()
        {
            if (_pendingReceiveTimeout != null)
            {
                _pendingReceiveTimeout.Cancel();
                _pendingReceiveTimeout = null;
            }
        }
    }
}

