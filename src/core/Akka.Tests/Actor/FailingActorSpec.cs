using Akka.Actor;
using Akka.TestKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Util;
using Akka.Routing;
using Xunit;
using System.Threading;

namespace Akka.Tests.Actor
{
    public class FailingActorSpec : AkkaSpec
    {

        public class FailingActor : ReceiveActor
        {

            public FailingActor()
            {
                Receive<string>(m => DoFail(m), m => "error".Equals(m));
            }

            private void DoFail(string m)
            {
                throw new Exception(m);
            }

        }

        public class ParentActor : ReceiveActor
        {
            private SupervisorStrategy strategy;
            private AutoResetEvent resetEvt;
            private IActorRef child;

            public ParentActor(SupervisorStrategy strategy, AutoResetEvent resetEvt)
            {
                this.strategy = strategy;
                this.resetEvt = resetEvt;

                Receive<string>(m => Become(CreateSimpleChild), m => "simple".Equals(m));
                Receive<string>(m => Become(CreatePooledChildBroadcast), m => "broadcastpool".Equals(m));
                Receive<string>(m => Become(CreatePooledChildRoundrobin), m => "roundrobinpool".Equals(m));
                Receive<string>(m => Become(CreatePooledChildRandom), m => "randompool".Equals(m));
                Receive<string>(m => Become(CreatePooledChildConsistentHashing), m => "consistenthashingpool".Equals(m));
                Receive<string>(m => Become(CreatePooledChildTailChopping), m => "tailchoppingpool".Equals(m));
                Receive<string>(m => Become(CreatePooledChildScatterGatherFirstCompleted), m => "scattergatherfirstcompletedpool".Equals(m));
                Receive<string>(m => Become(CreatePooledChildSmallestMailbox), m => "smallestmailboxpool".Equals(m));

                Receive<string>(m => Become(CreateGroupedChildBroadcast), m => "broadcastgroup".Equals(m));
                Receive<string>(m => Become(CreateGroupedChildRoundrobin), m => "roundrobingroup".Equals(m));
                Receive<string>(m => Become(CreateGroupedChildRandom), m => "randomgroup".Equals(m));
                Receive<string>(m => Become(CreateGroupedChildConsistentHashing), m => "consistenthashinggroup".Equals(m));
                Receive<string>(m => Become(CreateGroupedChildTailChopping), m => "tailchoppinggroup".Equals(m));
                Receive<string>(m => Become(CreateGroupedChildScatterGatherFirstCompleted), m => "scattergatherfirstcompletedgroup".Equals(m));
                Receive<string>(m => Become(CreateGroupedChildSmallestMailbox), m => "smallestmailboxgroup".Equals(m));
            }

            private void DoFail(string m)
            {
                throw new Exception(m);
            }

            private void CreateSimpleChild()
            {
                child = Context.ActorOf(Props.Create<FailingActor>(strategy));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildBroadcast()
            {
                child = Context.ActorOf(new BroadcastPool(1).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildRoundrobin()
            {
                child = Context.ActorOf(new RoundRobinPool(1).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildRandom()
            {
                child = Context.ActorOf(new RandomPool(1).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildConsistentHashing()
            {
                child = Context.ActorOf(new ConsistentHashingPool(1).WithHashMapping(o => o.GetHashCode()).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildTailChopping()
            {
                child = Context.ActorOf(new TailChoppingPool(1, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(20)).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildScatterGatherFirstCompleted()
            {
                child = Context.ActorOf(new ScatterGatherFirstCompletedPool(1).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreatePooledChildSmallestMailbox()
            {
                child = Context.ActorOf(new SmallestMailboxPool(1).WithSupervisorStrategy(strategy).Props(Props.Create<FailingActor>()));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildBroadcast()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildRoundrobin()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildRandom()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildConsistentHashing()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildTailChopping()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildScatterGatherFirstCompleted()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            private void CreateGroupedChildSmallestMailbox()
            {
                IActorRef realChild = Context.ActorOf(Props.Create<FailingActor>(), "failing");

                child = Context.ActorOf(Props.Empty.WithRouter(new BroadcastGroup(new string[] { realChild.Path.ToString() })));

                Receive<string>(m => child.Tell(m), m => true);

                resetEvt.Set();
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return strategy;
            }
        }

        public class ResumeDecider : IDecider
        {
            private long counter = 0;

            public long Counter
            {
                get { return Interlocked.Read(ref counter); }
            }

            public Directive Decide(Exception cause)
            {
                if (cause != null && cause.Message != null && cause.Message.Equals("error", StringComparison.InvariantCultureIgnoreCase))
                {
                    Interlocked.Increment(ref counter);
                }

                return Directive.Resume;
            }

        }


        [Fact]
        public void simple_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("simple");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void roundrobinpool_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("roundrobinpool");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var router = ((ActorRefWithCell)actor).Children.First();
            var child = ((RoutedActorRef)router).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void randompool_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("randompool");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var router = ((ActorRefWithCell)actor).Children.First();
            var child = ((RoutedActorRef)router).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void consistenthashingpool_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("consistenthashingpool");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var router = ((ActorRefWithCell)actor).Children.First();
            var child = ((RoutedActorRef)router).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void tailchoppingpool_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("tailchoppingpool");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var router = ((ActorRefWithCell)actor).Children.First();
            var child = ((RoutedActorRef)router).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void scattergatherfirstcompletedpool_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("scattergatherfirstcompletedpool");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var router = ((ActorRefWithCell)actor).Children.First();
            var child = ((RoutedActorRef)router).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void smallestmailboxpool_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("smallestmailboxpool");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var router = ((ActorRefWithCell)actor).Children.First();
            var child = ((RoutedActorRef)router).Children.OfType<LocalActorRef>().First();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }


        [Fact]
        public void roundrobingroup_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("roundrobingroup");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().FirstOrDefault();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void randomgroup_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("randomgroup");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().FirstOrDefault();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void consistenthashinggroup_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("consistenthashinggroup");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().FirstOrDefault();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void tailchoppinggroup_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("tailchoppinggroup");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().FirstOrDefault();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void scattergatherfirstcompletedgroup_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("scattergatherfirstcompletedgroup");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().FirstOrDefault();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void smallestmailboxgroup_child_actor_must_clear_currentmessage_after_failure_and_resume()
        {
            var decider = new ResumeDecider();
            var strategy = new OneForOneStrategy(decider);
            var resetEvt = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create<ParentActor>(strategy, resetEvt));

            actor.Tell("smallestmailboxgroup");

            resetEvt.WaitOne();

            actor.Tell("error");

            Thread.Sleep(500);

            var child = ((ActorRefWithCell)actor).Children.OfType<LocalActorRef>().FirstOrDefault();

            decider.Counter.ShouldBe(1);
            child.Cell.CurrentMessage.ShouldBe(null);
        }
    }
}
