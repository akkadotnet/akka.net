using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace RemoteBenchmark
{
    class Program
    {
        static void Main(string[] args)
        {
            var actorSystemProvider = new HeliosActorSystemProvider();
            var heliosSystemProvider = new HeliosActorSystemProvider();

            var system1 = actorSystemProvider.CreateSystem(8080);
            var system2 = actorSystemProvider.CreateSystem(8081);

            int concurrency = Environment.ProcessorCount;
            int messageCount = 20000;
            SemaphoreSlim ready = new SemaphoreSlim(0);
            ManualResetEventSlim start = new ManualResetEventSlim(false);

            if (args.Length == 1)
                concurrency = int.Parse(args[0]);

            var workers = new List<Task>();
            for (int i = 1; i <= concurrency; i++)
            {
                int index = i;
                workers.Add(RunThread(() =>
                {
                    var receiver = system1.ActorOf<ReceiverActor>("Receiver_" + index);
                    system2.ActorOf<EchoActor>("EchoActor_" + index);

                    string echoActorPath = actorSystemProvider.GetBaseAddress(8081) + "EchoActor_" + index;

                    var remoteEchoRef = system1.ActorSelection(echoActorPath).ResolveOne(Timeout.InfiniteTimeSpan).Result;

                    receiver.Ask(new ReceiverActor.Init(remoteEchoRef)).Wait();

                    ready.Release();

                    start.Wait();

                    var task = receiver.Ask(new ReceiverActor.PerformanceRun(messageCount));

                    for (int j = 0; j < messageCount; j++)
                    {
                        remoteEchoRef.Tell("a", ActorRefs.NoSender);
                    }

                    task.Wait();
                }));
            }

            for (int i = 0; i < concurrency; i++)
                ready.Wait();

            start.Set();
            Stopwatch sw = Stopwatch.StartNew();

            Task.WaitAll(workers.ToArray());

            Console.WriteLine("Message per seconds: " + messageCount * concurrency * 2 / sw.Elapsed.TotalSeconds);
        }

        private static Task RunThread(Action action)
        {
            return Task.Factory.StartNew(action, TaskCreationOptions.LongRunning);
        }
    }

    public abstract class ActorSystemProvider
    {
        public const string SystemName = "System";

        protected Config BaseConfig
        {
            get
            {
                return ConfigurationFactory.ParseString(@"
akka {
    stdout-loglevel = off
    loglevel = off
    actor {
        provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
        serializers {
            wire = ""Akka.Serialization.WireSerializer, Akka.Serialization.Wire""
        }
        serialization-bindings {
            ""System.Object"" = wire
        }
    }
    remote {
        log-received-messages = off
        log-sent-messages = off
    }
}
");
            }
        }

        protected abstract Config GetConfig(int port);

        public ActorSystem CreateSystem(int port)
        {
            return ActorSystem.Create(SystemName, GetConfig(port).WithFallback(BaseConfig));
        }

        public abstract string GetBaseAddress(int port);
    }

    public class NetworkStreamActorSystemProvider : ActorSystemProvider
    {
        protected override Config GetConfig(int port)
        {
            return ConfigurationFactory.ParseString(@"
akka {
    remote {
        enabled-transports = [""akka.remote.networkstream""]
        networkstream {
            transport-class = ""Akka.Remote.Transport.Streaming.NetworkStreamTransport, Akka.Remote""
            hostname = ""localhost""
            port = " + port + @"
        }
    }
}
");
        }

        public override string GetBaseAddress(int port)
        {
            return $"akka.tcp://{SystemName}@localhost:{port}/user/";
        }
    }

    public class HeliosActorSystemProvider : ActorSystemProvider
    {
        protected override Config GetConfig(int port)
        {
            return ConfigurationFactory.ParseString(@"
akka {
    remote {
        helios.tcp {
            transport-class = ""Akka.Remote.Transport.Helios.HeliosTcpTransport_v1_0, Akka.Remote""
            hostname = ""localhost""
            port = " + port + @"
        }
    }
}
");
        }

        public override string GetBaseAddress(int port)
        {
            return $"akka.tcp://{SystemName}@localhost:{port}/user/";
        }
    }

    public class ReceiverActor : UntypedActor
    {
        public class Init
        {
            public IActorRef EchoActor { get; private set; }

            public Init(IActorRef echoActor)
            {
                EchoActor = echoActor;
            }
        }

        public class PerformanceRun
        {
            public int MessageCount { get; private set; }

            public PerformanceRun(int messageCount)
            {
                MessageCount = messageCount;
            }
        }

        private IActorRef _init;
        private IActorRef _request;
        private int _total;
        private int _received;

        protected override void OnReceive(object message)
        {
            if (message is Init)
            {
                var init = (Init) message;
                _init = Sender;
                init.EchoActor.Tell("a");

                Become(WaitingForEcho);
            }
        }

        private void WaitingForEcho(object message)
        {
            _init.Tell("done");
            Become(Ready);
        }

        private void Ready(object message)
        {
            if (message is PerformanceRun)
            {
                var request = (PerformanceRun)message;
                _request = Sender;
                _total = request.MessageCount;

                Become(Running);
            }
        }

        private void Running(object message)
        {
            ++_received;

            if (_received >= _total)
                _request.Tell("done");
        }
    }

    public class EchoActor : UntypedActor
    {
        private IActorRef _sender;

        protected override void OnReceive(object message)
        {
            if (_sender == null)
                _sender = Sender;

            _sender.Tell(message, ActorRefs.NoSender);
        }
    }
}
