using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Pigeon.Actor
{
    public abstract class Mailbox
    {
        public Action<Envelope> OnNext { get; set; }
        public abstract void Post(Envelope message);

        public abstract void Stop();
    }

    public class DataFlowMailbox : Mailbox
    {
        ActionBlock<Envelope> inner;

        public DataFlowMailbox()
        {
            inner = new ActionBlock<Envelope>((Action<Envelope>)this.OnNextWrapper);
        }
        private void OnNextWrapper(Envelope envelope)
        {
            OnNext(envelope);
        }
        public override void Post(Envelope message)
        {
            inner.Post(message);
        }

        public override void Stop()
        {
            throw new NotImplementedException();
        }
    }


    /// <summary>
    /// For explorative reasons only
    /// </summary>
    public class ConcurrentQueueMailbox : Mailbox
    {
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> userMessages = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();
        private System.Collections.Concurrent.ConcurrentQueue<Envelope> systemMessages = new System.Collections.Concurrent.ConcurrentQueue<Envelope>();
        
        private WaitCallback handler = null;
        private volatile bool hasUnscheduledMessages = false;
        private volatile bool Stopped = false;
        private int status;

        private static class MailboxStatus
        {
            public const int Idle = 0;
            public const int Busy = 1;
        }

        private void Run(object _)
        {
            if (Stopped)
            {
            //    Console.WriteLine("dead {0}", System.Threading.Thread.CurrentThread.GetHashCode());
                return;
            }

          //  Console.WriteLine("working {0}", System.Threading.Thread.CurrentThread.GetHashCode());
            hasUnscheduledMessages = false;
            Envelope envelope;
            while (systemMessages.TryDequeue(out envelope))
            {           
                this.OnNext(envelope);           
            }
            int throttle = 20;
            int left = throttle;
            while (userMessages.TryDequeue(out envelope))
            {
                this.OnNext(envelope);
                if (systemMessages.TryDequeue(out envelope))
                {
                    this.OnNext(envelope);
                    break;
                }
                left--;
                if (left == 0)
                {
                    hasUnscheduledMessages = true;
                    break;
                }
            }

            Interlocked.Exchange(ref status, MailboxStatus.Idle);

            if (hasUnscheduledMessages)
            {
                hasUnscheduledMessages = false;
                Schedule();
            }
        }
        public ConcurrentQueueMailbox()
        {                        
        }

        private void Schedule()
        {
            //only schedule if we idle
            if (Interlocked.Exchange(ref status, MailboxStatus.Busy) == MailboxStatus.Idle)
            {
                ThreadPool.UnsafeQueueUserWorkItem(Run,null);
            }
        }

        public override void Post(Envelope envelope)
        {
            hasUnscheduledMessages = true;
            if (envelope.Payload is SystemMessage)
            {
                systemMessages.Enqueue(envelope);
            }
            else
            {
                userMessages.Enqueue(envelope);
            }

            Schedule();
        }

        public override void Stop()
        {
            Stopped = true;
        }
    }

    public abstract class MessageDispatcher
    {
        public const int DefaultThroughput = 5;
        public abstract void Schedule(Action<object> run);
    }

    public class ThreadPoolDispatcher : MessageDispatcher
    {

        public override void Schedule(Action<object> run)
        {
            WaitCallback wc = new WaitCallback(run);
            ThreadPool.UnsafeQueueUserWorkItem(wc,null);
        }
    }

    public class NaiveThreadPoolDispatcher : MessageDispatcher
    {
        public override void Schedule(Action<object> run)
        {
            NaiveThreadPool.Schedule(run);
        }
    }
    public static class NaiveThreadPool
    {
        private static ConcurrentQueue<Action<object>> queue = new ConcurrentQueue<Action<object>>();
        private static List<Thread> threads = new List<Thread>();
        static NaiveThreadPool()
        {
            BlockingCollection<Action<object>> b = new BlockingCollection<Action<object>>(queue);
            int threadCount = 20;
            for (int i = 0; i < threadCount; i++)
            {
                var t = new Thread(_ =>
                    {
                        while (true)
                        {                            
                            var action = b.Take();
                            action(null);
                        }
                    });
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
        }

        public static void Schedule(Action<object> run)
        {
            queue.Enqueue(run);
        }
    }
}
