//-----------------------------------------------------------------------
// <copyright file="MessageSerializerRemotingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Tests.Serialization
{
    internal class Configs
    {
        public static readonly Config CustomSerializers = ConfigurationFactory.ParseString(@"
akka.actor {
  serializers {
    my-payload = ""Akka.Persistence.Tests.Serialization.MyPayloadSerializer, Akka.Persistence.Tests""
    old-payload = ""Akka.Persistence.Tests.Serialization.OldPayloadSerializer, Akka.Persistence.Tests""
    testserializer = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
  }
  serialization-bindings {
    ""Akka.Persistence.Tests.Serialization.MyPayload, Akka.Persistence.Tests"" = my-payload
    ""System.Object"" = testserializer
    # this entry was used when creating the data for the test
    # ""deserialize data when class is removed""
    #""Akka.Persistence.Tests.Serialization.OldPayload, Akka.Persistence.Tests"" = old-payload
  }
}");
        public static readonly Config Remote = ConfigurationFactory.ParseString(@"
akka {
  actor {
    provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
  }
  remote {
    dot-netty.tcp {
      applied-adapters = []
      transport-protocol = tcp
      port = 0
      hostname = ""127.0.0.1""
      port = 0
    }
  }
  loglevel = ERROR
  log-dead-letters = 0
  log-dead-letters-during-shutdown = off
}");

        public static Config Config(params string[] configs)
        {
            return configs.Aggregate(ConfigurationFactory.Empty,
                (r, c) => r.WithFallback(ConfigurationFactory.ParseString(c)));
        }
        
        public static Config Config(params Config[] configs)
        {
            return configs.Aggregate(ConfigurationFactory.Empty,
                (r, c) => r.WithFallback(c));
        }
    }

    public class MyPayload
    {
        public string Data { get; private set; }

        public MyPayload(string data)
        {
            Data = data;
        }

        protected bool Equals(MyPayload other)
        {
            return string.Equals(Data, other.Data);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((MyPayload) obj);
        }

        public override int GetHashCode()
        {
            return (Data != null ? Data.GetHashCode() : 0);
        }
    }

    // TODO: temporary disabled
    public class MessageSerializerRemotingSpec : AkkaSpec
    {
        internal class LocalActor : ActorBase
        {
            private readonly int _port;

            public LocalActor(int port)
            {
                _port = port;
            }

            protected override bool Receive(object message)
            {
                Context.ActorSelection(string.Format("akka.tcp://remote@127.0.0.1:{0}/user/remote", _port))
                    .Tell(message, ActorRefs.NoSender);
                return true;
            }
        }

        internal class RemoteActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                if (message is Persistent)
                {
                    var p = (Persistent) message;
                    if (p.Payload is MyPayload)
                    {
                        p.Sender.Tell("p" + ((MyPayload) p.Payload).Data);
                    }
                    else return false;
                }
                else if (message is AtomicWrite)
                {
                    var a = (AtomicWrite) message;
                    foreach (var p in (IEnumerable<IPersistentRepresentation>) a.Payload)
                    {
                        if (p.Payload is MyPayload)
                        {
                            p.Sender.Tell("p" + ((MyPayload) p.Payload).Data);
                        }
                    }
                }
                else return false;
                return true;
            }
        }

        private readonly ActorSystem _remoteSystem;
        private readonly IActorRef _localActor;

        private readonly Akka.Serialization.Serialization _serialization;

        public MessageSerializerRemotingSpec() : base(
            Configs.Config(
                Configs.Remote,
                Configs.CustomSerializers,
                ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf") // for akka-persistence-message
                ))
        {
            _remoteSystem = ActorSystem.Create("remote",
                Configs.Remote.WithFallback(Configs.CustomSerializers)
                    .WithFallback(ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf")));
            _localActor = Sys.ActorOf(Props.Create(() => new LocalActor(Port(_remoteSystem))), "local");

            _serialization = Sys.Serialization;

            _remoteSystem.ActorOf(Props.Create(() => new RemoteActor()), "remote");
        }

        private int Port(ActorSystem system)
        {
            return Address(system).Port.Value;
        }

        private Address Address(ActorSystem system)
        {
            return ((ExtendedActorSystem) system).Provider.DefaultAddress;
        }

        protected override void AfterTermination()
        {
            _remoteSystem.Terminate().Wait(TimeSpan.FromSeconds(2));
            base.AfterTermination();
        }

        [Fact]
        public void MessageSerializer_should_custom_serialize_Persistent_messages_during_remoting()
        {
            // this also verifies serialization of Persistent.Sender,
            // because the RemoteActor will reply to the Persistent.Sender
            _localActor.Tell(new Persistent(new MyPayload("a"), sender: TestActor));
            ExpectMsg("p.a.");
        }

        [Fact]
        public void MessageSerializer_should_custom_serialize_AtomicWrite_messages_during_remoting()
        {
            var p1 = new Persistent(new MyPayload("a"), sender: TestActor);
            var p2 = new Persistent(new MyPayload("b"), sender: TestActor);
            _localActor.Tell(new AtomicWrite(ImmutableList.Create(new IPersistentRepresentation[] {p1, p2})));
            Within(5.Seconds(), () => { 
                ExpectMsg("p.a.");
                ExpectMsg("p.b.");
            });
        }
    }
}
