//-----------------------------------------------------------------------
// <copyright file="PersistentViewSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public partial class PersistentViewSpec : PersistenceSpec
    {
        protected IActorRef _pref;
        protected IActorRef _view;
        protected TestProbe _prefProbe;
        protected TestProbe _viewProbe;

        public PersistentViewSpec()
            : base(Configuration("inmem", "PersistentViewSpec"))
        {
            _prefProbe = CreateTestProbe();
            _viewProbe = CreateTestProbe();

            _pref = ActorOf(() => new TestPersistentActor(Name, _prefProbe.Ref));
            _pref.Tell("a");
            _pref.Tell("b");

            _prefProbe.ExpectMsg("a-1");
            _prefProbe.ExpectMsg("b-2");
        }

        protected override void AfterAll()
        {
            if (Sys != null)
            {
                Sys.Stop(_pref);
                Sys.Stop(_view);
            }
            base.AfterAll();
        }

        [Fact]
        public void PersistentView_should_receive_past_updates_from_persistent_actor()
        {
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
        }

        [Fact]
        public void PersistentView_should_receive_live_updates_from_persistent_actor()
        {
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _pref.Tell("c");
            _viewProbe.ExpectMsg("replicated-c-3");
        }

        [Fact]
        public void PersistentView_should_run_updates_at_specified_interval()
        {
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref, TimeSpan.FromSeconds(2), null));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _pref.Tell("c");
            _viewProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));
            _viewProbe.ExpectMsg("replicated-c-3");
        }

        [Fact]
        public void PersistentView_should_run_updates_on_user_request()
        {
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref, TimeSpan.FromSeconds(5), null));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _pref.Tell("c");
            _prefProbe.ExpectMsg("c-3");
            _view.Tell(new Update(isAwait: false));
            _viewProbe.ExpectMsg("replicated-c-3");
        }

        [Fact]
        public void PersistentView_should_run_updates_on_user_request_and_wait_for_update()
        {
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref, TimeSpan.FromSeconds(5), null));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _pref.Tell("c");
            _prefProbe.ExpectMsg("c-3");
            _view.Tell(new Update(isAwait: false));
            _view.Tell("get");
            _viewProbe.ExpectMsg("replicated-c-3");
        }

        [Fact]
        public void PersistentView_should_run_updates_again_on_failure_outside_an_update_cycle()
        {
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref, TimeSpan.FromSeconds(5), null));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _view.Tell("boom");
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
        }

        [Fact]
        public void PersistentView_should_run_updates_again_on_failure_during_an_update_cycle()
        {
            _pref.Tell("c");
            _prefProbe.ExpectMsg("c-3");
            _view = ActorOf(() => new TestPersistentView(Name, _viewProbe.Ref, TimeSpan.FromSeconds(5), "b"));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _viewProbe.ExpectMsg("replicated-c-3");
        }

        [Fact(Skip = "FIXME: working, but random timeouts can occur when running all tests at once")]
        public void PersistentView_should_run_size_limited_updates_on_user_request()
        {
            _pref.Tell("c");
            _pref.Tell("d");
            _pref.Tell("e");
            _pref.Tell("f");
            _prefProbe.ExpectMsg("c-3");
            _prefProbe.ExpectMsg("d-4");
            _prefProbe.ExpectMsg("e-5");
            _prefProbe.ExpectMsg("f-6");

            //TODO: performance optimization 
            _view = ActorOf(() => new PassiveTestPersistentView(Name, _viewProbe.Ref, null));
            _view.Tell(new Update(isAwait: true, replayMax: 2));
            _view.Tell("get");
            _viewProbe.ExpectMsg("replicated-b-2");

            _view.Tell(new Update(isAwait: true, replayMax: 1));
            _view.Tell("get");
            _viewProbe.ExpectMsg("replicated-c-3");

            _view.Tell(new Update(isAwait: true, replayMax: 4));
            _view.Tell("get");
            _viewProbe.ExpectMsg("replicated-f-6");
        }

        [Fact]
        public void PersistentView_should_run_size_limited_updates_automatically()
        {
            var replayProbe = CreateTestProbe();
            _pref.Tell("c");
            _pref.Tell("d");
            _prefProbe.ExpectMsg("c-3");
            _prefProbe.ExpectMsg("d-4");

            SubscribeToReplay(replayProbe);

            _view = ActorOf(() => new ActiveTestPersistentView(Name, _viewProbe.Ref));

            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _viewProbe.ExpectMsg("replicated-c-3");
            _viewProbe.ExpectMsg("replicated-d-4");

            replayProbe.ExpectMsg<ReplayMessages>(m => m.FromSequenceNr == 1L && m.Max == 2L);
            replayProbe.ExpectMsg<ReplayMessages>(m => m.FromSequenceNr == 3L && m.Max == 2L);
            replayProbe.ExpectMsg<ReplayMessages>(m => m.FromSequenceNr == 5L && m.Max == 2L);
        }

        [Fact]
        public void PersistentView_should_support_Context_Become()
        {
            _view = ActorOf(() => new BecomingPersistentView(Name, _viewProbe.Ref));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
        }

        [Fact]
        public void PersistentView_should_check_if_incoming_message_is_persistent()
        {
            _pref.Tell("c");
            _prefProbe.ExpectMsg("c-3");

            _view = ActorOf(() => new PersistentOrNotTestPersistentView(Name, _viewProbe.Ref));
            _view.Tell("d");
            _view.Tell("e");

            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _viewProbe.ExpectMsg("replicated-c-3");
            _viewProbe.ExpectMsg("normal-d-3");
            _viewProbe.ExpectMsg("normal-e-3");

            _pref.Tell("f");
            _viewProbe.ExpectMsg("replicated-f-4");
        }

        [Fact]
        public void PersistentView_should_take_snapshots()
        {
            _view = ActorOf(() => new SnapshottingPersistentView(Name, _viewProbe.Ref));
            _viewProbe.ExpectMsg("replicated-a-1");
            _viewProbe.ExpectMsg("replicated-b-2");
            _view.Tell("snap");
            _viewProbe.ExpectMsg("snapped");
            _view.Tell("restart");
            _pref.Tell("c");
            _viewProbe.ExpectMsg("replicated-b-2");
            _viewProbe.ExpectMsg("replicated-c-3");
        }

        private void SubscribeToReplay(TestProbe probe)
        {
            Sys.EventStream.Subscribe(probe.Ref, typeof(ReplayMessages));
        }
    }
}

