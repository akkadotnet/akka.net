//-----------------------------------------------------------------------
// <copyright file="ActorCell.ReceiveTimeout.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
            if (!Mailbox.HasMessages)
            {
                if (_receiveTimeoutDuration != null)
                {
                    _pendingReceiveTimeout.Cancel(); //Cancel any ongoing task
                    _pendingReceiveTimeout = System.Scheduler.ScheduleTellOnceCancelable(_receiveTimeoutDuration.Value,
                        Self, Akka.Actor.ReceiveTimeout.Instance, Self);
                }
                else
                {
                    CancelReceiveTimeout();
                }
            }
            else
            {
                CancelReceiveTimeout();
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

