using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        public void CheckReceiveTimeout()
        {
            CancelReceiveTimeout();
            if (_receiveTimeoutDuration != null && !Mailbox.HasMessages)
            {
                _pendingReceiveTimeout = System.Scheduler.ScheduleTellOnceCancelable(_receiveTimeoutDuration.Value, Self, ReceiveTimeout.Instance, Self);
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
