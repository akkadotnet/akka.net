//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace FaultTolerance
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = ConfigurationFactory.ParseString("akka.loglevel = DEBUG \n akka.actor.debug.lifecycle = on");

            var system = ActorSystem.Create("FaultToleranceSample", config);
            var worker = system.ActorOf<Worker>("worker");
            var listener = system.ActorOf<Listener>("listener");

            // start the work and listen on progress
            // note that the listener is used as sender of the tell,
            // i.e. it will receive replies from the worker
            worker.Tell("Start", listener);

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }
    }

    #region Listener
    
    // Listens on progress from the worker and shuts down the system when enough work has been done.
    public class Listener : UntypedActor
    {
        ILoggingAdapter log = Logging.GetLogger(Context);

        protected override void PreRestart(Exception reason, object message)
        {
            // If we don't get any progress within 15 seconds then the service is unavailable
            Context.SetReceiveTimeout(TimeSpan.FromSeconds(15));
        }

        protected override void OnReceive(object message)
        {
            log.Debug("Received message {0}", message);

            if (message is Progress)
            {
                var progress = (Progress) message;

                log.Info("Current progress: {0:N}%", progress.Percent);

                if (progress.Percent >= 100)
                {
                    log.Info("That's all, shutting down");
                    Context.System.Terminate();
                }
            }
            else if (message == ReceiveTimeout.Instance)
            {
                // No progress within 15 seconds, ServiceUnavailable
                log.Error("Shutting down due to unavailable service");
                Context.System.Terminate();
            }
            else
            {
                Unhandled(message);
            }
        }
    }

    #endregion

    #region Worker Messages

    public class Progress
    {
        public Progress(double percent)
        {
            this.Percent = percent;
        }

        public double Percent { get; private set; }

        public override string ToString()
        {
            return String.Format("Progress({0:N2})", Percent);
        }
    }

    #endregion

    #region Worker

    // Worker performs some work when it receives the Start message. It will
    // continuously notify the sender of the Start message of current Progress.
    // The Worker supervise the CounterService.
    public class Worker : UntypedActor
    {
        ILoggingAdapter log = Logging.GetLogger(Context);

        // The sender of the initial Start message will continuously be notified about progress
        IActorRef progressListener;
        IActorRef counterService = Context.ActorOf<CounterService>("counter");
        int totalCount = 51;

        // Stop the CounterService child if it throws ServiceUnavailable
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                if (ex is ServiceUnavailableException)
                    return Directive.Stop;

                return Directive.Escalate;
            });
        }

        protected override void OnReceive(object message)
        {
            log.Debug("Received message {0}", message);

            if (message.Equals("Start") && progressListener == null)
            {
                progressListener = Sender;
                Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(TimeSpan.Zero, TimeSpan.FromSeconds(1), Self, "Do", Self);
            }
            else if (message.Equals("Do"))
            {
                counterService.Tell(new Increment(1));
                counterService.Tell(new Increment(1));
                counterService.Tell(new Increment(1));

                // Send current progress to the initial sender
                counterService.Ask<CurrentCount>("GetCurrentCount", TimeSpan.FromSeconds(5))
                    .ContinueWith(t => new Progress(100.0 * t.Result.Count / totalCount))
                    .PipeTo(progressListener);
            }
            else
            {
                Unhandled(message);
            }
        }
    }

    #endregion

    #region CounterService Messages

    public class CurrentCount
    {
        public CurrentCount(string key, long count)
        {
            this.Key = key;
            this.Count = count;
        }

        public string Key { get; private set; }
        public long Count { get; private set; }

        public override string ToString()
        {
            return String.Format("CurrentCount ({0}, {1})", Key, Count);
        }
    }

    public class Increment
    {
        public Increment(int n)
        {
            this.N = n;
        }

        public long N { get; private set; }

        public override string ToString()
        {
            return String.Format("Increment({0})", N);
        }
    }

    public class ServiceUnavailableException : Exception
    {
        public ServiceUnavailableException(string message)
            : base(message)
        { }
    }

    #endregion
    
    #region CounterService
    
    // Adds the value received in Increment message to a persistent counter.
    // Replies with CurrentCount when it is asked for CurrentCount. CounterService
    // supervise Storage and Counter.
    public class CounterService : UntypedActor
    {
        ILoggingAdapter log = Logging.GetLogger(Context);

        string key = Context.Self.Path.Name;
        IActorRef storage;
        IActorRef counter;
        List<SenderMessagePair> backlog = new List<SenderMessagePair>();
        int MAX_BACKLOG = 10000;

        // Restart the storage child when StorageException is thrown.
        // After 3 restarts within 5 seconds it will be stopped.
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(3, TimeSpan.FromSeconds(5), ex =>
            {
                if (ex is StorageException)
                    return Directive.Restart;

                return Directive.Escalate;
            });
        }

        protected override void PreStart()
        {
            InitStorage();
        }

        // The child storage is restarted in case of failure, but after 3 restarts,
        // and still failing it will be stopped. Better to back-off than
        // continuously failing. When it has been stopped we will schedule a
        // Reconnect after a delay. Watch the child so we receive Terminated message
        // when it has been terminated.
        private void InitStorage()
        {
            storage = Context.Watch(Context.ActorOf<Storage>("storage"));

            // Tell the counter, if any, to use the new storage
            if (counter != null)
                counter.Tell(new UseStorage(storage));

            // We need the initial value to be able to operate
            storage.Tell(new Get(key));
        }

        protected override void OnReceive(object message)
        {
            if (String.Format("{0}", message) == "System.Object")
                Debugger.Break();

            log.Debug("Received message {0}", message);

            var entry = message as Entry;

            if (entry != null && entry.Key == key && counter == null)
            {
                // Reply from Storage of the initial value, now we can create the Counter
                counter = Context.ActorOf(Props.Create<Counter>(key, entry.Value));

                // Tell the counter to use current storage
                counter.Tell(new UseStorage(storage));

                // and send the buffered backlog to the counter
                foreach (var e in backlog)
                    counter.Tell(e.Message, e.Sender);

                backlog.Clear();
            }
            else if (message is Increment || message.Equals("GetCurrentCount"))
            {
                ForwardOrPlaceInBacklog(message);
            }
            else if (message is Terminated)
            {
                // After 3 restarts the storage child is stopped.
                // We receive Terminated because we watch the child, see InitStorage.
                storage = null;

                // Tell the counter that there is no storage for the moment
                counter.Tell(new UseStorage(null));

                // Try to re-establish storage after while
                Context.System.Scheduler.ScheduleTellOnceCancelable(TimeSpan.FromSeconds(10), Self, "Reconnect", Self);
            }
            else if (message.Equals("Reconnect"))
            {
                // Re-establish storage after the scheduled delay
                InitStorage();
            }
            else
            {
                Unhandled(message);
            }
        }

        void ForwardOrPlaceInBacklog(object message)
        {
            // We need the initial value from storage before we can start delegate to
            // the counter. Before that we place the messages in a backlog, to be sent
            // to the counter when it is initialized.
            
            if (counter == null)
            {
                if (backlog.Count >= MAX_BACKLOG)
                    throw new ServiceUnavailableException("CounterService not available, lack of initial value");

                backlog.Add(new SenderMessagePair(Sender, message));
            }
            else
            {
                counter.Forward(message);
            }
        }

        private class SenderMessagePair
        {
            public SenderMessagePair(IActorRef sender, object message)
            {
                this.Sender = sender;
                this.Message = message;
            }

            public IActorRef Sender { get; private set; }
            public object Message { get; private set; }
        }
    }

    #endregion

    #region Counter Messages

    public class UseStorage
    {
        public UseStorage(IActorRef storage)
        {
            this.Storage = storage;
        }

        public IActorRef Storage { get; private set; }

        public override string ToString()
        {
            return String.Format("UseStorage({0})", Storage);
        }
    }

    #endregion

    #region Counter

    // The in memory count variable that will send current value to the Storage,
    // if there is any storage available at the moment.
    public class Counter : UntypedActor
    {
        ILoggingAdapter log = Logging.GetLogger(Context);
        string key;
        long count;
        IActorRef storage;

        public Counter(string key, long initialValue)
        {
            this.key = key;
            this.count = initialValue;
        }

        protected override void OnReceive(object message)
        {
            log.Debug("Received message {0}", message);

            if (message is UseStorage)
            {
                storage = ((UseStorage)message).Storage;
                StoreCount();
            }
            else if (message is Increment)
            {
                count += ((Increment) message).N;
                StoreCount();
            }
            else if (message.Equals("GetCurrentCount"))
            {
                log.Warning("CurrentCount is {0}", count);
                Sender.Tell(new CurrentCount(key, count));
            }
            else
            {
                Unhandled(message);
            }
        }

        private void StoreCount()
        {
            // Delegate dangerous work, to protect our valuable state.
            // We can continue without storage.
            if (storage != null)
                storage.Tell(new Store(new Entry(key, count)));
        }
    }

    #endregion

    #region Storage Messages

    public class Store
    {
        public Store(Entry entry)
        {
            this.Entry = entry;
        }

        public Entry Entry { get; private set; }

        public override string ToString()
        {
            return String.Format("Store({0})", Entry);
        }
    }

    public class Entry
    {
        public Entry(string key, long value)
        {
            this.Key = key;
            this.Value = value;
        }
        public string Key { get; private set; }
        public long Value { get; private set; }

        public override string ToString()
        {
            return String.Format("Entry({0}, {1})", Key, Value);
        }
    }

    public class Get
    {
        public Get(string key)
        {
            this.Key = key;
        }

        public string Key { get; private set; }

        public override string ToString()
        {
            return String.Format("Get({0})", Key);
        }
    }

    #endregion

    #region Storage

    // Saves key/value pairs to persistent storage when receiving Store message.
    // Replies with current value when receiving Get message. Will throw
    // StorageException if the underlying data store is out of order.
    public class Storage : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            if (message is Store)
            {
                var entry = ((Store)message).Entry;
                DummyDB.Save(entry.Key, entry.Value);
            }
            else if (message is Get)
            {
                var key = ((Get)message).Key;
                var value = DummyDB.Load(key);

                Sender.Tell(new Entry(key, value));
            }
            else
            {
                Unhandled(message);
            }
        }
    }

    #endregion

    #region DummyDB

    public class DummyDB
    {
        private static Dictionary<string, long> db = new Dictionary<string, long>();

        public static void Save(string key, long value)
        {
            if (11 <= value && value <= 14)
                throw new StorageException("Simulated store failure: " + value);

            lock (db)
            {
                db[key] = value;
            }
        }

        public static long Load(string key)
        {
            long value;

            lock (db)
            {
                if (db.TryGetValue(key, out value))
                    return value;
            }

            return 0;
        }
    }

    public class StorageException : Exception
    {
        public StorageException(string message)
            : base(message)
        { }
    }

    #endregion
}

