using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch
{
    public abstract class MessageDispatcher
    {
        public const int DefaultThroughput = 5;

        protected MessageDispatcher()
        {
            Throughput = DefaultThroughput;
        }

        public long? ThroughputDeadlineTime { get; set; }
        public int Throughput { get; set; }

        public abstract void Schedule(Action<object> run);
    }

    public class ThreadPoolDispatcher : MessageDispatcher
    {
        public override void Schedule(Action<object> run)
        {
            var wc = new WaitCallback(run);
            ThreadPool.UnsafeQueueUserWorkItem(wc, null);
        }
    }

    /// <summary>
    ///     Dispatcher that dispatches messages on the current synchronization context, e.g. WinForms or WPF GUI thread
    /// </summary>
    public class CurrentSynchronizationContextDispatcher : MessageDispatcher
    {
        private readonly TaskScheduler scheduler;

        public CurrentSynchronizationContextDispatcher()
        {
            scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        public override void Schedule(Action<object> run)
        {
            var t = new Task(() => run(null));
            t.Start(scheduler);
        }
    }

    public class SingleThreadDispatcher : MessageDispatcher
    {
        private readonly ConcurrentQueue<Action<object>> queue = new ConcurrentQueue<Action<object>>();
        private volatile bool running = true;

        public SingleThreadDispatcher()
        {
            var b = new BlockingCollection<Action<object>>(queue);
            var t = new Thread(_ =>
            {
                while (running)
                {
                    Action<object> next = b.Take();
                    next(null);
                }
            });
        }

        public override void Schedule(Action<object> run)
        {
            queue.Enqueue(run);
        }
    }

    public class Dispatchers
    {
        public static string DefaultDispatcherId;
        private readonly ActorSystem system;

        public Dispatchers(ActorSystem system)
        {
            this.system = system;
        }

        public static MessageDispatcher FromCurrentSynchronizationContext()
        {
            return new CurrentSynchronizationContextDispatcher();
        }

        public MessageDispatcher FromConfig(string path)
        {
            //TODO: this should not exist, it is only here because we dont serialize dispathcer when doing remote deploy..
            if (path == null)
            {
                var disp = new ThreadPoolDispatcher();
                disp.Throughput = 100;
                return disp;
            }

            Config config = system.Settings.Config.GetConfig(path);
            string type = config.GetString("type");
            int throughput = config.GetInt("throughput");
            //shutdown-timeout
            //throughput-deadline-time
            //attempt-teamwork
            //mailbox-requirement

            MessageDispatcher dispatcher = null;
            switch (type)
            {
                case "Dispatcher":
                    dispatcher = new ThreadPoolDispatcher();
                    break;
                case "PinnedDispatcher":
                    dispatcher = new SingleThreadDispatcher();
                    break;
                case "SynchronizedDispatcher":
                    dispatcher = new CurrentSynchronizationContextDispatcher();
                    break;
                default:
                    Type dispatcherType = Type.GetType(type);
                    dispatcher = (MessageDispatcher) Activator.CreateInstance(dispatcherType);
                    break;
            }

            dispatcher.Throughput = throughput;
            //  dispatcher.ThroughputDeadlineTime 

            return dispatcher;
        }
    }
}