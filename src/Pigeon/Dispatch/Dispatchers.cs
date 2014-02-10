using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Dispatch
{
    public abstract class MessageDispatcher
    {
        public const int DefaultThroughput = 5;        
        public long? ThroughputDeadlineTime { get; set; }
        public int Throughput { get; set; }

        protected MessageDispatcher()
        {
            Throughput = DefaultThroughput;
        }

        public abstract void Schedule(Action<object> run);
    }

    public class ThreadPoolDispatcher : MessageDispatcher
    {

        public override void Schedule(Action<object> run)
        {
            WaitCallback wc = new WaitCallback(run);
            ThreadPool.UnsafeQueueUserWorkItem(wc, null);
        }
    }

    public class SingleThreadDispatcher : MessageDispatcher
    {
        private volatile bool running = true;
        private ConcurrentQueue<Action<object>> queue = new ConcurrentQueue<Action<object>>();
        public SingleThreadDispatcher()
        {
            BlockingCollection<Action<object>> b = new BlockingCollection<Action<object>>(queue);
            var t = new Thread(_ =>
            {
                while (running)
                {
                    var next = b.Take();
                    next(null);
                }
            });
        }

        public override void Schedule(Action<object> run)
        {
            queue.Enqueue(run);
        }
    }
}
