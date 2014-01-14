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
                return;
            }

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
                if (left == 0 && userMessages.TryPeek(out envelope)) 
                {
                    // we have processed throughput messages, and there are still envelopes left
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
                //NaiveThreadPool.Schedule(Run);
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
    public static class NaiveThreadPool
    {
        private static volatile int next;
        private static SingleThreadWorker[] workers;
        static NaiveThreadPool()
        {
            
            int threadCount = 20;
            workers = new SingleThreadWorker[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                workers[i] = new SingleThreadWorker();
            }
        }

        public static void Schedule(Action<object> task)
        {
            workers[next++ % workers.Length].Schedule(task);
        }
    }

    public class SingleThreadWorker
    {
        private readonly ConcurrentQueue<Action<object>> _queue = new ConcurrentQueue<Action<object>>();
        private volatile bool _hasMoreTasks;
        private volatile bool _running = true;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        public SingleThreadWorker()
        {
            var thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "worker" + Guid.NewGuid(),
            };

            thread.Start();
        }

        private void Run()
        {
            while (_running)
            {
                _signal.WaitOne();
                do
                {
                    _hasMoreTasks = false;

                    Action<object> task;
                    while (_queue.TryDequeue(out task) && _running)
                    {
                        task(null);
                    }
                    //wait a short while to let _hasMoreTasks to maybe be set to true
                    //this avoids the roundtrip to the AutoResetEvent
                    //that is, if there is intense pressure on the pool, we let some new
                    //tasks have the chance to arrive and be processed w/o signaling
                    if (!_hasMoreTasks)
                        Thread.Sleep(5);

                    busy = false;

                } while (_hasMoreTasks);
            }
        }

        public void Schedule(Action<object> task)
        {
            _hasMoreTasks = true;
            _queue.Enqueue(task);

            SetSignal();
        }

        private volatile bool busy = false;
        private void SetSignal()
        {
            if (!busy)
            {
                busy = true;
                _signal.Set();
            }
        }
    }
}
