using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Class MessageDispatcher.
    /// </summary>
    public abstract class MessageDispatcher
    {
        /// <summary>
        ///     The default throughput
        /// </summary>
        public const int DefaultThroughput = 5;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageDispatcher" /> class.
        /// </summary>
        protected MessageDispatcher()
        {
            Throughput = DefaultThroughput;
        }

        /// <summary>
        ///     Gets or sets the throughput deadline time.
        /// </summary>
        /// <value>The throughput deadline time.</value>
        public long? ThroughputDeadlineTime { get; set; }

        /// <summary>
        ///     Gets or sets the throughput.
        /// </summary>
        /// <value>The throughput.</value>
        public int Throughput { get; set; }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public abstract void Schedule(Action<object> run);
    }

    /// <summary>
    ///     Class ThreadPoolDispatcher.
    /// </summary>
    public class ThreadPoolDispatcher : MessageDispatcher
    {
        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
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
        /// <summary>
        ///     The scheduler
        /// </summary>
        private readonly TaskScheduler scheduler;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CurrentSynchronizationContextDispatcher" /> class.
        /// </summary>
        public CurrentSynchronizationContextDispatcher()
        {
            scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public override void Schedule(Action<object> run)
        {
            var t = new Task(() => run(null));
            t.Start(scheduler);
        }
    }

    /// <summary>
    ///     Class SingleThreadDispatcher.
    /// </summary>
    public class SingleThreadDispatcher : MessageDispatcher
    {
        /// <summary>
        ///     The queue
        /// </summary>
        private readonly ConcurrentQueue<Action<object>> queue = new ConcurrentQueue<Action<object>>();

        /// <summary>
        ///     The running
        /// </summary>
        private volatile bool running = true;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SingleThreadDispatcher" /> class.
        /// </summary>
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

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public override void Schedule(Action<object> run)
        {
            queue.Enqueue(run);
        }
    }

    /// <summary>
    ///     Class Dispatchers.
    /// </summary>
    public class Dispatchers
    {
        /// <summary>
        ///     The default dispatcher identifier
        /// </summary>
        public static string DefaultDispatcherId;

        /// <summary>
        ///     The system
        /// </summary>
        private readonly ActorSystem system;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Dispatchers" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        public Dispatchers(ActorSystem system)
        {
            this.system = system;
        }

        /// <summary>
        ///     Froms the current synchronization context.
        /// </summary>
        /// <returns>MessageDispatcher.</returns>
        public static MessageDispatcher FromCurrentSynchronizationContext()
        {
            return new CurrentSynchronizationContextDispatcher();
        }

        /// <summary>
        ///     Froms the configuration.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>MessageDispatcher.</returns>
        public MessageDispatcher FromConfig(string path)
        {
            //TODO: this should not exist, it is only here because we dont serialize dispathcer when doing remote deploy..
            if (string.IsNullOrEmpty(path))
            {
                var disp = new ThreadPoolDispatcher
                {
                    Throughput = 100
                };
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
                    if (dispatcherType == null)
                    {
                        throw new NotSupportedException("Could not resolve dispatcher type " + type);
                    }
                    dispatcher = (MessageDispatcher) Activator.CreateInstance(dispatcherType);
                    break;
            }

            dispatcher.Throughput = throughput;
            //  dispatcher.ThroughputDeadlineTime 

            return dispatcher;
        }
    }
}