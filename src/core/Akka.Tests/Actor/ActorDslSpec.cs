using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorDslSpec : AkkaSpec
    {
        [Fact]
        public void A_ligthweight_creator_must_support_creating_regular_actors()
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
                if (msg == "info")
                    TestActor.Tell("A");
                else if (msg == "switch")
                    c.BecomeStacked((msg2, ctx2) =>
                    {
                        if (msg2 == "info")
                            TestActor.Tell("B");
                        else if (msg2 == "switch")
                            c.UnbecomeStacked();
                    });
                else if (msg == "lobotomize")
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
        public void A_ligthweight_creator_must_support_actor_setup_and_teardown()
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

        [Fact]
        public void A_ligthweight_creator_must_support_restart()
        {
            //TODO: requires event filters
        }

        [Fact]
        public void A_ligthweight_creator_must_support_supervising()
        {
            //TODO: requires event filters
        }

        [Fact]
        public void A_ligthweight_creator_must_support_nested_declarations()
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

        [Fact]
        public void A_ligthweight_creator_must_support_stash()
        {
            //TODO: requires proven and tested stash implementation
        }
    }
}