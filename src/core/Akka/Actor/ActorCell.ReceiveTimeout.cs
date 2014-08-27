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
        private CancellationTokenSource _pendingReceiveTimeout = null;

		public void SetReceiveTimeout(TimeSpan? timeout=null)
        {
            _receiveTimeoutDuration = timeout;
        }

        public void CheckReceiveTimeout()
        {
            //TODO: This is expensive, ca 30% of throughput is lost here
			if (!Mailbox.HasMessages)
            {
                if (_receiveTimeoutDuration != null)
                {
                    CancelReceiveTimeout();
                    _pendingReceiveTimeout = new CancellationTokenSource();
                    System.Scheduler.ScheduleOnce(_receiveTimeoutDuration.Value, Self, ReceiveTimeout.Instance, _pendingReceiveTimeout.Token);
                }
                else
                {
                    CancelReceiveTimeout();                    
                }
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
