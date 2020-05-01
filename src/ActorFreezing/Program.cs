using Akka.Actor;
using Akka.Routing;
using Akka.Dispatch;
using Akka.Configuration;
using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ActorFreezing
{
    public class CustomRouter<TActor> : ReceiveActor where TActor : ActorBase
    {
        private readonly uint _nrOfInstance;
        private IActorRef[] _routees;
        private uint _invocationNr = 0;

        public CustomRouter(uint nrOfInstance)
        {
            _nrOfInstance = nrOfInstance;

            Receive<GetRoutees>(routees =>
            {
                var actorRefRoutees = _routees.Select(@ref => new ActorRefRoutee(@ref));
                var resp = new Routees(actorRefRoutees);
                Sender.Tell(resp);
            });

            ReceiveAny(HandleAny);
        }

        protected override void PreStart()
        {
            _routees = new IActorRef[_nrOfInstance];

            var props = Props.Create<TActor>();

            for (var i = 0; i < _nrOfInstance; i++)
            {
                var actorRef = Context.ActorOf(props);
                _routees[i] = actorRef;
            }
        }

        protected override void PreRestart(Exception reason, object message)
        {
            // avoid killing children
        }

        protected override void PostRestart(Exception reason)
        {
            _routees = Context.GetChildren().ToArray();
            // avoid calling PreStart
        }

        private void HandleAny(object msg)
        {
            _routees[_invocationNr % _nrOfInstance].Forward(msg);

            unchecked
            {
                _invocationNr++;
            }
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(Decider.From(Directive.Escalate));
        }
    }

    public class SimpleActor : ReceiveActor
    {
        private readonly object _lock = new object();
        private static int Counter = 0;
        private readonly ILoggingAdapter _log = Logging.GetLogger(Context);

        public SimpleActor()
        {
            Receive<int>(HandleInt);
        }

        private void HandleInt(int obj)
        {
            lock(_lock)
            {
                Counter++;
            }
            if (obj == 1)
                throw new InvalidOperationException($"I'm dead. #{Counter}");

            _log.Info($"Received msg {obj}");
            Sender.Tell(obj);
        }
    }

    public class Program
    {
        static readonly TimeSpan _delay = TimeSpan.FromSeconds(0.03);

        static async Task Main(string[] args)
        {
            var hocon = @"
akka.loglevel=DEBUG
akka.actor.debug {
  receive = on
  autoreceive = on
  lifecycle = on
  fsm = on
  event-stream = on
  unhandled = on
  router-misconfiguration = on
}";

            var actorSystem = ActorSystem.Create("Test", ConfigurationFactory.ParseString(hocon));

            var poolProps = Props.Create<SimpleActor>().WithRouter(new RoundRobinPool(10));
            var poolActorRef = actorSystem.ActorOf(poolProps, "freeze-test");

            await Task.Delay(100); // small delay to give time for setup

            // has errors when multiple escalations. some of routees stop working
            Console.WriteLine("Starting pool tests");
            await TestPool(poolActorRef);

        }

        private static async Task TestPool(IActorRef poolActorRef)
        {
            /*
            Console.WriteLine("Hit 'Enter' to start warm-up with exception msgs");
            Console.ReadLine();

            var routees = (await poolActorRef.Ask<Routees>(GetRoutees.Instance))
                .Members
                .OfType<ActorRefRoutee>();
            PrintRoutees(routees);

            for (int i = 0; i < 100; i++)
            {
                poolActorRef.Tell(1);
            }

            await Task.Delay(TimeSpan.FromSeconds(5)); // small delay to give time to reach convergence

            _log.Info("Finished sending exception warm-up msgs");

            Console.WriteLine("Hit 'Enter' to start pinging");
            Console.ReadLine();

            routees = (await poolActorRef.Ask<Routees>(GetRoutees.Instance))
                .Members
                .OfType<ActorRefRoutee>();

            var isSuspendedMethodInfo = typeof(Mailbox).GetMethod("IsSuspended",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.InvokeMethod);

            foreach (var actorRefRoutee in routees)
            {
                var actorCell = ((LocalActorRef)actorRefRoutee.Actor).Cell;
                var result = (bool)isSuspendedMethodInfo.Invoke(actorCell.Mailbox, null);

                if (result)
                {
                    _log.Info("Sending two msgs but not output expected: mailbox status had SuspendMask");
                }
                else
                {
                    _log.Info("Sending two msgs. Output expected");
                }

                //poolActorRef.Tell(2);
                //actorRefRoutee.Send(2, ActorRefs.NoSender);

                try
                {
                    var poolResult = await poolActorRef.Ask<int>(2, TimeSpan.FromSeconds(1));
                    _log.Info($"pool ask result: {poolResult}");
                    var routeeResult = await actorRefRoutee.Ask(2, TimeSpan.FromSeconds(1));
                    _log.Info($"worker ask result: {routeeResult}");
                } catch(Exception e)
                {
                    _log.Warning($"Ask failed. Exception: {e.GetType()}");
                }

                await Task.Delay(TimeSpan.FromSeconds(1));

                await Task.Delay(TimeSpan.FromSeconds(5)); // small delay to give time to reach convergence
            }
            */

            /*
            _log.Info("Hit 'Enter' to ask 100 fails");
            Console.ReadLine();
            await AskActor(poolActorRef, 100, 1);

            _log.Info("Hit 'Enter' to ask 20 success");
            Console.ReadLine();
            await AskActor(poolActorRef, 20, 2);

            _log.Info("Hit 'Enter' to tell 100 fails with delay");
            Console.ReadLine();
            await TellActor(poolActorRef, 100, 1, _delay);

            _log.Info("Hit 'Enter' to ask 20 success");
            Console.ReadLine();
            await AskActor(poolActorRef, 20, 2);
            */
            var routees = (await poolActorRef.Ask<Routees>(GetRoutees.Instance))
                .Members
                .OfType<ActorRefRoutee>();
            PrintRoutees(routees);

            Console.WriteLine("Hit 'Enter' to tell 50 fails with no delay");
            Console.ReadLine();
            await TellActor(poolActorRef, 5, 1);
            await Task.Delay(2000);
            PrintRoutees(routees);

            Console.WriteLine("Hit 'Enter' to ask 20 success");
            Console.ReadLine();
            await AskActor(poolActorRef, 20, 2);

            Console.WriteLine("Hit 'Enter' to ask 50 fails");
            Console.ReadLine();
            await AskActor(poolActorRef, 50, 1, _delay);

            Console.WriteLine("Hit 'Enter' to ask 50 success");
            Console.ReadLine();
            await AskActor(poolActorRef, 50, 2);

            Console.WriteLine("Finished pinging");
        }

        static async Task AskActor(IActorRef actor, int count, int payload, TimeSpan? delay = null)
        {
            var failCount = 0;
            if (delay == null) delay = _delay;

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var r = await actor.Ask<int>(payload, delay);
                    Console.WriteLine($"Ask result: {r}");
                }
                catch
                {
                    failCount++;
                }
            }
            if(failCount == 0)
                Console.WriteLine($"[SUCCESS] {failCount}/{count} message failed.");
            else
                Console.WriteLine($"[FAILED] {failCount}/{count} message failed.");
        }

        static async Task TellActor(IActorRef actor, int count, int payload, TimeSpan? delay = null)
        {
            var failCount = 0;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    actor.Tell(payload);
                    if (delay.HasValue)
                        await Task.Delay(delay.Value);
                }
                catch
                {
                    failCount++;
                }
            }

            if (!delay.HasValue)
                await Task.Delay(1000);

            if (failCount == 0)
                Console.WriteLine($"[SUCCESS] {failCount}/{count} message failed.");
            else
                Console.WriteLine($"[FAILED] {failCount}/{count} message failed.");
        }

        public static void PrintRoutees(IEnumerable<ActorRefRoutee> routees)
        {
            foreach (var routeesMember in routees)
            {
                var cell = ((LocalActorRef)routeesMember.Actor).Cell;
                Console.WriteLine($"[{routeesMember.Actor.Path}] Suspended: {cell.Mailbox.IsSuspended()}, Suspend status: {cell.Mailbox.CurrentStatus()}");
            }
        }
    }
}
