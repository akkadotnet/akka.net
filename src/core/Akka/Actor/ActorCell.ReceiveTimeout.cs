//-----------------------------------------------------------------------
// <copyright file="ActorCell.ReceiveTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{	
    public partial class ActorCell
    {
        private TimeSpan? _receiveTimeoutDuration = null;
        private ICancelable _pendingReceiveTimeout = null;

		public void SetReceiveTimeout(TimeSpan? timeout=null)
        {
            _receiveTimeoutDuration = timeout;
        }

        public TimeSpan? ReceiveTimeout
        {
            get
            {
                return _receiveTimeoutDuration;
            }
        }

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

