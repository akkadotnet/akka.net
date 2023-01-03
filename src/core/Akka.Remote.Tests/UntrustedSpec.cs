//-----------------------------------------------------------------------
// <copyright file="UntrustedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
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

            EventFilter.Debug().Mute();
        }

        private async Task<IActorRef> RemoteDaemon()
        {
            var p = CreateTestProbe(_client);
            _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements)
                .Tell(new IdentifyReq("/remote"), p.Ref);
            return (await p.ExpectMsgAsync<ActorIdentity>()).Subject;
        }

        private async Task<IActorRef> Target2()
        {
            var p = CreateTestProbe(_client);
            _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements)
                .Tell(new IdentifyReq("child2"), p.Ref);

            return (await p.ExpectMsgAsync<ActorIdentity>()).Subject;
        }


        protected override async Task AfterAllAsync()
        {
            await base.AfterAllAsync();
            await ShutdownAsync(_client);
        }


        [Fact]
        public async Task Untrusted_mode_must_allow_actor_selection_to_configured_white_list()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements);
            sel.Tell("hello");
            await ExpectMsgAsync("hello");
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_harmful_messages_to_slash_remote()
        {
            var logProbe = CreateTestProbe();
            // but instead install our own listener
            Sys.EventStream.Subscribe(
                Sys.ActorOf(Props.Create(() => new DebugSniffer(logProbe)).WithDeploy(Deploy.Local), "debugSniffer"),
                typeof (Debug));

            var remoteDaemon = await RemoteDaemon(); 
            remoteDaemon.Tell("hello");
            await logProbe.ExpectMsgAsync<Debug>();
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_harmful_messages_to_test_actor()
        {
            var target2 = await Target2();
            var remoteDaemon = await RemoteDaemon();

            target2.Tell(new Terminated(remoteDaemon, true, false));
            target2.Tell(PoisonPill.Instance);
            _client.Stop(target2);
            target2.Tell("blech");
            await ExpectMsgAsync("blech");
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_watch_messages()
        {
            var target2 = await Target2();
            _client.ActorOf(Props.Create(() => new Target2Watch(target2, TestActor)).WithDeploy(Deploy.Local));
            _receptionist.Tell(new StopChild1("child2"));
            await ExpectMsgAsync("child2 stopped");
            // no Terminated msg, since watch was discarded
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_actor_selection()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/TestActor.Path.Elements);
            sel.Tell("hello");
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_actor_selection_to_child_of_matching_white_list()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements/"child1");
            sel.Tell("hello");
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_actor_selection_with_wildcard()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements/"*");
            sel.Tell("hello");
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task Untrusted_mode_must_discard_actor_selection_containing_harmful_message()
        {
            var sel = _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements);
            sel.Tell(PoisonPill.Instance);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
        }


        [Fact]
        public async Task Untrusted_mode_must_discard_actor_selection_with_non_root_anchor()
        {
            var p = CreateTestProbe(_client);
            _client.ActorSelection(new RootActorPath(_address)/_receptionist.Path.Elements)
                .Tell(new Identify(null), p.Ref);
            var clientReceptionistRef = (await p.ExpectMsgAsync<ActorIdentity>()).Subject;

            var sel = ActorSelection(clientReceptionistRef, _receptionist.Path.ToStringWithoutAddress());
            sel.Tell("hello");
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
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
                switch (message)
                {
                    case IdentifyReq req:
                        var actorSelection = Context.ActorSelection(req.Path);
                        actorSelection.Tell(new Identify(null), Sender);
                        return true;
                    case StopChild1 child:
                        Context.Stop(Context.Child(child.Name));
                        return true;
                    default:
                        _testActor.Forward(message);
                        return true;
                }
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
                _testActor.Tell($"{Self.Path.Name} stopped");
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
                if (message is Debug debug)
                {
                    if (((string) debug.Message).Contains("dropping"))
                    {
                        _testProbe.Ref.Tell(debug);
                    }
                    return true;
                }

                return false;
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
