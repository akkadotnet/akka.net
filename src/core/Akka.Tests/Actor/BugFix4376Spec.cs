using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    /// <summary>
    /// Spec for https://github.com/akkadotnet/akka.net/issues/4376
    /// </summary>
    public class BugFix4376Spec : AkkaSpec
    {
        public BugFix4376Spec(ITestOutputHelper output): base(output) { }

        private readonly TimeSpan _delay = TimeSpan.FromSeconds(0.08);

        private class SimpleActor : ReceiveActor
        {
            private readonly object _lock = new object();
            private static int Counter = 0;

            public SimpleActor()
            {
                Receive<int>(i => i == 1, c => {
                    lock (_lock)
                        Counter++;
                    throw new InvalidOperationException($"I'm dead. #{Counter}");
                });

                Receive<int>(i => i == 2, c => {
                    Sender.Tell(2);
                });
            }
        }

        private class SimpleBroadcastActor : ReceiveActor
        {
            private readonly AtomicCounter _counter = null;
            private readonly object _lock = new object();
            private static int Counter = 0;

            public SimpleBroadcastActor(AtomicCounter counter)
            {
                _counter = counter;

                Receive<int>(i => i == 1, c => {
                    lock (_lock)
                        Counter++;
                    throw new InvalidOperationException($"I'm dead. #{Counter}");
                });

                Receive<int>(i => i == 2, c => {
                    _counter.AddAndGet(1);
                    Sender.Tell(2);
                });
            }
        }

        private class ParentActor : ReceiveActor
        {
            private readonly AtomicCounter _counter;
            private readonly List<IActorRef> _children = new List<IActorRef>();

            public ParentActor(AtomicCounter counter)
            {
                _counter = counter;

                for(var i = 0; i < 10; ++i)
                {
                    var child = Context.ActorOf(Props.Create<SimpleActor>(), $"child-{i}");
                    _children.Add(child);
                }

                ReceiveAsync<string>(str => str.Equals("spam-fails"), async m =>
                {
                    foreach (var child in _children)
                    {
                        child.Tell(1);
                    }
                    await Task.Delay(1000);
                });

                ReceiveAsync<string>(str => str.Equals("run-test"), async m =>
                {
                    for (var i = 0; i < 2; ++i)
                    {
                        foreach (var child in _children)
                        {
                            try
                            {
                                await child.Ask<int>(1, TimeSpan.FromSeconds(0.08));
                            } catch 
                            {
                                _counter.AddAndGet(1);
                            }
                        }
                    }
                });
            }
        }

        [Fact]
        public async Task Supervisor_with_RoundRobin_Pool_router_should_handle_multiple_child_failure()
        {
            var poolProps = Props.Create<SimpleActor>().WithRouter(new RoundRobinPool(10));
            var poolActorRef = Sys.ActorOf(poolProps, "roundrobin-pool-freeze-test");

            // rapidly fail children. the router should handle children failing
            // while itself is still being recreated
            for (var i = 0; i < 10; i++)
            {
                poolActorRef.Tell(1);
            }

            var failCount = 0;
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    await poolActorRef.Ask<int>(2, _delay);
                }
                catch
                {
                    failCount++;
                }
            }
            failCount.Should().Be(0);
        }

        [Fact]
        public async Task Supervisor_with_Random_Pool_router_should_handle_multiple_child_failure()
        {
            var poolProps = Props.Create<SimpleActor>().WithRouter(new RandomPool(10));
            var poolActorRef = Sys.ActorOf(poolProps, "random-pool-freeze-test");

            // rapidly fail children. the router should handle children failing
            // while itself is still being recreated
            for (var i = 0; i < 10; i++)
            {
                poolActorRef.Tell(1);
            }

            var failCount = 0;
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    await poolActorRef.Ask<int>(2, _delay);
                }
                catch
                {
                    failCount++;
                }
            }
            failCount.Should().Be(0);
        }

        [Fact]
        public async Task Supervisor_with_Broadcast_Pool_router_should_handle_multiple_child_failure()
        {
            var poolActorRef = Sys.ActorOf(
                new BroadcastPool(5)
                .Props(Props.Create<SimpleActor>()));

            // rapidly fail children. the router should handle children failing
            // while itself is still being recreated
            for (var i = 0; i < 20; i++)
            {
                poolActorRef.Tell(1);
            }

            poolActorRef.Tell(2);
            ExpectMsg<int>();
            ExpectMsg<int>();
            ExpectMsg<int>();
            ExpectMsg<int>();
            ExpectMsg<int>();
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Supervisor_with_RoundRobin_Group_router_should_handle_multiple_child_failure()
        {
            const int connectionCount = 10;
            var doneLatch = new TestLatch(connectionCount);

            var replies = new Dictionary<string, int>();
            for (int i = 1; i <= connectionCount; i++)
            {
                replies["target-" + i] = 0;
            }

            var paths = Enumerable.Range(1, connectionCount).Select(n =>
            {
                var routee = Sys.ActorOf(Props.Create(() => new SimpleActor()), "target-" + n);
                return routee.Path.ToStringWithoutAddress();
            });

            var groupProps = Props.Empty
                .WithRouter(new RoundRobinGroup(paths))
                .WithSupervisorStrategy(new OneForOneStrategy(Decider.From(Directive.Escalate)));
            var groupActorRef = Sys.ActorOf(groupProps, "round-robin-group1");

            // rapidly fail children. the router should handle children failing
            // while itself is still being recreated
            for (var i = 0; i < 20; i++)
            {
                groupActorRef.Tell(1);
            }

            var failCount = 0;
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    await groupActorRef.Ask<int>(2, _delay);
                }
                catch
                {
                    failCount++;
                }
            }
            failCount.Should().Be(0);
        }

        [Fact]
        public async Task Supervisor_with_Random_Group_router_should_handle_multiple_child_failure()
        {
            const int connectionCount = 10;
            var doneLatch = new TestLatch(connectionCount);

            var replies = new Dictionary<string, int>();
            for (int i = 1; i <= connectionCount; i++)
            {
                replies["target-" + i] = 0;
            }

            var paths = Enumerable.Range(1, connectionCount).Select(n =>
            {
                var routee = Sys.ActorOf(Props.Create(() => new SimpleActor()), "target-" + n);
                return routee.Path.ToStringWithoutAddress();
            });

            var groupProps = Props.Empty
                .WithRouter(new RandomGroup(paths))
                .WithSupervisorStrategy(new OneForOneStrategy(Decider.From(Directive.Escalate)));
            var groupActorRef = Sys.ActorOf(groupProps, "random-group1");

            // rapidly fail children. the router should handle children failing
            // while itself is still being recreated
            for (var i = 0; i < 20; i++)
            {
                groupActorRef.Tell(1);
            }

            var failCount = 0;
            for (var i = 0; i < 20; i++)
            {
                try
                {
                    await groupActorRef.Ask<int>(2, _delay);
                }
                catch
                {
                    failCount++;
                }
            }
            failCount.Should().Be(0);
        }

        [Fact]
        public async Task Supervisor_should_handle_multiple_child_failure()
        {
            var counter = new AtomicCounter(0);

            var supervisor = Sys.ActorOf(Props.Create<ParentActor>(counter), "supervisor");
            supervisor.Tell("spam-fails");
            supervisor.Tell("run-test");

            await Task.Delay(1000);
            counter.Current.Should().Be(0);
        }
    }
}
