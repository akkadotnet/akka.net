using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;

namespace Akka.TestKit
{
    class ActorCellKeepingSynchronizationContext : SynchronizationContext
    {
        private readonly ActorCell _cell;

        public ActorCellKeepingSynchronizationContext(ActorCell cell)
        {
            _cell = cell;
        }


        public override void Post(SendOrPostCallback d, object state)
        {
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                var oldCell = InternalCurrentActorCellKeeper.Current;
	            var oldContext = Current;
				SetSynchronizationContext(this);
                InternalCurrentActorCellKeeper.Current = _cell;
				
                try
                {
                    d(state);
                }
                finally
                {
                    InternalCurrentActorCellKeeper.Current = oldCell;
					SetSynchronizationContext(oldContext);
                }
            }, state);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            var tcs = new TaskCompletionSource<int>();
            Post(_ =>
            {
                try
                {
                    d(state);
                    tcs.SetResult(0);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
            }, state);
            tcs.Task.Wait();
        }
    }
}
 