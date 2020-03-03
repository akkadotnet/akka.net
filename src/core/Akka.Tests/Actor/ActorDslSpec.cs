//-----------------------------------------------------------------------
// <copyright file="ActorDslSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorDslSpec : AkkaSpec
    {
        [Fact]
        public void A_lightweight_creator_must_support_creating_regular_actors()
        {
            var a = Sys.ActorOf(Props.Create(() => new Act(c =>
                c.Receive<string>(msg => msg == "hello", (msg, ctx) => TestActor.Tell("hi")))));

            a.Tell("hello");
            ExpectMsg("hi");
        }

        [Fact]
        public void A_lightweight_creator_must_support_become_stacked()
        {
            var a = Sys.ActorOf(c => c.Become((msg, ctx) =>
            {
                var message = msg as string;
                if (message == null) return;

                if (message == "info")
                    TestActor.Tell("A");
                else if (message == "switch")
                    c.BecomeStacked((msg2, ctx2) =>
                    {
                        var message2 = msg2 as string;
                        if (message2 == null) return;
                        
                        if (message2 == "info")
                            TestActor.Tell("B");
                        else if (message2 == "switch")
                            c.UnbecomeStacked();
                    });
                else if (message == "lobotomize")
                    c.UnbecomeStacked();
            }));

            a.Tell("info");
            ExpectMsg("A");

            a.Tell("switch");
            a.Tell("info");
            ExpectMsg("B");

            a.Tell("switch");
            a.Tell("info");
            ExpectMsg("A");
        }

        [Fact]
        public void A_lightweight_creator_must_support_actor_setup_and_teardown()
        {
            const string started = "started";
            const string stopped = "stopped";

            var a = Sys.ActorOf(c =>
            {
                c.OnPreStart = _ => TestActor.Tell(started);
                c.OnPostStop = _ => TestActor.Tell(stopped);
            });

            Sys.Stop(a);
            ExpectMsg(started);
            ExpectMsg(stopped);
        }

        [Fact(Skip = "TODO: requires event filters")]
        public void A_lightweight_creator_must_support_restart()
        {
            //TODO: requires event filters
        }

        [Fact(Skip = "TODO: requires event filters")]
        public void A_lightweight_creator_must_support_supervising()
        {
            //TODO: requires event filters
        }

        [Fact]
        public void A_lightweight_creator_must_support_nested_declarations()
        {
            var a = Sys.ActorOf(act =>
            {
                var b = act.ActorOf(act2 =>
                {
                    act2.OnPreStart = context => context.Parent.Tell("hello from " + context.Self.Path);
                }, "barney");
                act.ReceiveAny((x, _) => TestActor.Tell(x));
            }, "fred");

            ExpectMsg("hello from akka://" + Sys.Name + "/user/fred/barney");
            LastSender.ShouldBe(a);
        }

        [Fact(Skip = "TODO: requires proven and tested stash implementation")]
        public void A_lightweight_creator_must_support_stash()
        {
            //TODO: requires proven and tested stash implementation
        }

        [Fact]
        public void A_lightweight_creator_must_support_actor_base_method_calls()
        {
            var parent = Sys.ActorOf(act =>
            {
                var child = act.ActorOf(act2 =>
                {
                    act2.OnPostStop = _ => TestActor.Tell("stopping child");
                    act2.Receive("ping", (msg, _) => TestActor.Tell("pong"));
                }, "child");
                act.OnPreRestart = (exc, msg, ctx) =>
                {
                    TestActor.Tell("restarting parent");
                    act.DefaultPreRestart(exc, msg);    //Will stop the children
                };
                act.Receive("crash",(m,ctx)=>{throw new Exception("Received <crash>");});
                act.ReceiveAny((x, _) => child.Tell(x));
            }, "parent");
            
            parent.Tell("ping");
            ExpectMsg("pong");

            parent.Tell("crash");
            ExpectMsg("restarting parent");
            ExpectMsg("stopping child");
        }

        [Fact]
        public void A_lightweight_creator_must_support_async_receives()
        {
            var parent = Sys.ActorOf(act =>
            {
                var completedTask = Task.FromResult(true);
                var child = act.ActorOf(act2 =>
                {
                    act2.ReceiveAsync<string>(m => m == "ping", (_, __) =>
                    {
                        TestActor.Tell("pong");
                        return completedTask;
                    });

                    act2.ReceiveAsync<string>((_, __) =>
                    {
                        TestActor.Tell("ping");
                        return completedTask;
                    }, msg => msg == "pong");

                    act2.ReceiveAsync<string>((_, __) =>
                    {
                        TestActor.Tell("hello");
                        return completedTask;
                    });
                });

                act.ReceiveAnyAsync((msg, _) => 
                {
                    child.Tell(msg);
                    return Task.FromResult(true);
                });
            });

            parent.Tell("ping");
            ExpectMsg("pong");

            parent.Tell("pong");
            ExpectMsg("ping");

            parent.Tell("hi");
            ExpectMsg("hello");
        }
    }
}

