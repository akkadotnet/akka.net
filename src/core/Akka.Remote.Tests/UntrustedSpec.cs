//-----------------------------------------------------------------------
// <copyright file="UntrustedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests
{
    public class UntrustedSpec : AkkaSpec
    {
        private readonly ActorSystem _client;
        private readonly Address _address;
        private readonly IActorRef _receptionist;
        private readonly Lazy<IActorRef> _remoteDaemon;
        private readonly Lazy<IActorRef> _target2;


        public UntrustedSpec(ITestOutputHelper output)
            : base(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.untrusted-mode = on
            akka.remote.trusted-selection-paths = [""/user/receptionist"", ]    
            akka.remote.dot-netty.tcp = {
                port = 0
                hostname = localhost
            }
            akka.loglevel = DEBUG
            ", output)
        {
            _client = ActorSystem.Create("UntrustedSpec-client", ConfigurationFactory.ParseString(@"
                akka.actor.provider =  ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                 akka.remote.dot-netty.tcp = {
                    port = 0
                    hostname = localhost
                }                
            ").WithFallback(Sys.Settings.Config));

            _address = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _receptionist = Sys.ActorOf(Props.Create(() => new Receptionist(TestActor)), "receptionist");

            _remoteDaemon = new Lazy<IActorRef>(() =>
            {
                var p = CreateTestProbe(_client);
                _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements)
                    .Tell(new IdentifyReq("/remote"), p.Ref);
                return p.ExpectMsg<ActorIdentity>().Subject;
            });

            _target2 = new Lazy<IActorRef>(() =>
            {
                var p = CreateTestProbe(_client);
                _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements)
                    .Tell(new IdentifyReq("child2"), p.Ref);

                var actorRef = p.ExpectMsg<ActorIdentity>().Subject;
                return actorRef;
            });


            EventFilter.Debug().Mute();
        }


        protected override void AfterTermination()
        {
            Shutdown(_client);
        }


        [Fact]
        public void Untrusted_mode_must_allow_actor_selection_to_configured_white_list()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements);
            sel.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void Untrusted_mode_must_discard_harmful_messages_to_slash_remote()
        {
            var logProbe = CreateTestProbe();
            // but instead install our own listener
            Sys.EventStream.Subscribe(
                Sys.ActorOf(Props.Create(() => new DebugSniffer(logProbe)).WithDeploy(Deploy.Local), "debugSniffer"),
                typeof (Debug));

            _remoteDaemon.Value.Tell("hello");
            logProbe.ExpectMsg<Debug>();
        }

        [Fact]
        public void Untrusted_mode_must_discard_harmful_messages_to_test_actor()
        {
            var target2 = _target2.Value;

            target2.Tell(new Terminated(_remoteDaemon.Value, true, false));
            target2.Tell(PoisonPill.Instance);
            _client.Stop(target2);
            target2.Tell("blech");
            ExpectMsg("blech");
        }

        [Fact]
        public void Untrusted_mode_must_discard_watch_messages()
        {
            var target2 = _target2.Value;
            _client.ActorOf(Props.Create(() => new Target2Watch(target2, TestActor)).WithDeploy(Deploy.Local));
            _receptionist.Tell(new StopChild1("child2"));
            ExpectMsg("child2 stopped");
            // no Terminated msg, since watch was discarded
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Untrusted_mode_must_discard_actor_selection()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/TestActor.Path.Elements);
            sel.Tell("hello");
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Untrusted_mode_must_discard_actor_selection_to_child_of_matching_white_list()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements/"child1");
            sel.Tell("hello");
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Untrusted_mode_must_discard_actor_selection_with_wildcard()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements/"*");
            sel.Tell("hello");
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Untrusted_mode_must_discard_actor_selection_containing_harmful_message()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements);
            sel.Tell(PoisonPill.Instance);
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }


        [Fact]
        public void Untrusted_mode_must_discard_actor_selection_with_non_root_anchor()
        {
            var p = CreateTestProbe(_client);
            _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements).Tell(
                new Identify(null), p.Ref);
            var clientReceptionistRef = p.ExpectMsg<ActorIdentity>().Subject;

            var sel = ActorSelection(clientReceptionistRef, _receptionist.Path.ToStringWithoutAddress());
            sel.Tell("hello");
            ExpectNoMsg(TimeSpan.FromSeconds(1));
        }


        private class IdentifyReq
        {
            public IdentifyReq(string path)
            {
                Path = path;
            }

            public string Path { get; }
        }

        private class StopChild1
        {
            public StopChild1(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }


        private class Receptionist : ActorBase
        {
            private readonly IActorRef _testActor;

            public Receptionist(IActorRef testActor)
            {
                _testActor = testActor;
                Context.ActorOf(Props.Create(() => new Child(testActor)), "child1");
                Context.ActorOf(Props.Create(() => new Child(testActor)), "child2");
                Context.ActorOf(Props.Create(() => new FakeUser(testActor)), "user");
            }


            protected override bool Receive(object message)
            {
                return message.Match().With<IdentifyReq>(req =>
                {
                    var actorSelection = Context.ActorSelection(req.Path);
                    actorSelection.Tell(new Identify(null), Sender);
                })
                    .With<StopChild1>(child => { Context.Stop(Context.Child(child.Name)); })
                    .Default(o => { _testActor.Forward(o); })
                    .WasHandled;
            }
        }

        private class Child : ActorBase
        {
            private readonly IActorRef _testActor;

            public Child(IActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override bool Receive(object message)
            {
                _testActor.Forward(message);
                return true;
            }

            protected override void PostStop()
            {
                _testActor.Tell(string.Format("{0} stopped", Self.Path.Name));
                base.PostStop();
            }
        }

        private class FakeUser : ActorBase
        {
            private readonly IActorRef _testActor;

            public FakeUser(IActorRef testActor)
            {
                _testActor = testActor;
                Context.ActorOf(Props.Create(() => new Child(testActor)), "receptionist");
            }

            protected override bool Receive(object message)
            {
                _testActor.Forward(message);
                return true;
            }
        }

        private class DebugSniffer : ActorBase
        {
            private readonly TestProbe _testProbe;

            public DebugSniffer(TestProbe testProbe)
            {
                _testProbe = testProbe;
            }

            protected override bool Receive(object message)
            {
                return message.Match().With<Debug>(debug =>
                {
                    if (((string) debug.Message).Contains("dropping"))
                    {
                        _testProbe.Ref.Tell(debug);
                    }
                }).WasHandled;
            }
        }

        private class Target2Watch : ActorBase
        {
            private readonly IActorRef _testActor;


            public Target2Watch(IActorRef target2, IActorRef testActor)
            {
                _testActor = testActor;
                Context.Watch(target2);
            }

            protected override bool Receive(object message)
            {
                _testActor.Forward(message);
                return true;
            }
        }
    }
}
