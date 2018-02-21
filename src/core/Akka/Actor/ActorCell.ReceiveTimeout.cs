//-----------------------------------------------------------------------
// <copyright file="ActorCell.ReceiveTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface INotInfluenceReceiveTimeout
    {
    }

    public partial class ActorCell
    {
        private TimeSpan? _receiveTimeoutDuration = null;
        private ICancelable _pendingReceiveTimeout = null;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        public void SetReceiveTimeout(TimeSpan? timeout=null)
        {
            _receiveTimeoutDuration = timeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan? ReceiveTimeout
        {
            get
            {
                return _receiveTimeoutDuration;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void CheckReceiveTimeout()
        {
            CancelReceiveTimeout();
            if (_receiveTimeoutDuration != null && !Mailbox.HasMessages)
            {
                _pendingReceiveTimeout = System.Scheduler.ScheduleTellOnceCancelable(_receiveTimeoutDuration.Value, Self, Akka.Actor.ReceiveTimeout.Instance, Self);
            }
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

