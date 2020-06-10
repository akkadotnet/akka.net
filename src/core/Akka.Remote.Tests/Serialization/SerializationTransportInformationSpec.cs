//-----------------------------------------------------------------------
// <copyright file="SerializationTransportInformationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Serialization
{

    public class TestMessage
    {
        public TestMessage(IActorRef @from, IActorRef to)
        {
            From = @from;
            To = to;
        }

        public IActorRef From { get; }

        public IActorRef To { get; }
    }

    public class JsonSerTestMessage
    {
        public JsonSerTestMessage(IActorRef @from, IActorRef to)
        {
            From = @from;
            To = to;
        }

        public IActorRef From { get; }

        public IActorRef To { get; }
    }


    public class TestSerializer : SerializerWithStringManifest
    {
        public TestSerializer(ExtendedActorSystem system) : base(system)
        {

        }

        public override int Identifier => 666;

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case TestMessage test:
                    VerifyTransportInfo();
                    var fromStr = Akka.Serialization.Serialization.SerializedActorPath(test.From);
                    var toStr = Akka.Serialization.Serialization.SerializedActorPath(test.To);
                    return Encoding.UTF8.GetBytes($"{fromStr},{toStr}");
            }

            throw new Exception($"Unrecognized message type {obj}");
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            VerifyTransportInfo();
            switch (manifest)
            {
                case "A":
                    var parts = Encoding.UTF8.GetString(bytes).Split(',');
                    var fromStr = parts[0];
                    var toStr = parts[1];
                    var from = system.Provider.ResolveActorRef(fromStr);
                    var to = system.Provider.ResolveActorRef(toStr);
                    return new TestMessage(from, to);
            }

            throw new Exception($"Unrecognized message type {manifest}");
        }

        public override string Manifest(object o)
        {
            return "A";
        }

        private void VerifyTransportInfo()
        {
            switch (Akka.Serialization.Serialization.CurrentTransportInformation)
            {
                case null:
                    throw new InvalidOperationException("CurrentTransportInformation was not set");
                case Information t:
                    if (!t.System.Equals(system))
                        throw new InvalidOperationException($"wrong system in CurrentTransportInformation, {t.System} != {system}");
                    if (t.Address != system.Provider.DefaultAddress)
                        throw new InvalidOperationException(
                            $"wrong address in CurrentTransportInformation, {t.Address} != {system.Provider.DefaultAddress}");
                    break;
            }
        }
    }

    public abstract class AbstractSerializationTransportInformationSpec : AkkaSpec
    {
        public static readonly Config Config = @"
            akka {
                loglevel = info
                actor
                {
                    provider = remote
                    serialize-creators = off
                    serializers
                    {
                        test = ""Akka.Remote.Serialization.SerializationTransportInformationSpec+TestSerializer, Akka.Remote.Tests""
                    }
                    serialization-bindings
                    {
                        ""Akka.Remote.Serialization.SerializationTransportInformationSpec+TestMessage, Akka.Remote.Tests"" = test
                        ""Akka.Remote.Serialization.SerializationTransportInformationSpec+JsonSerTestMessage, Akka.Remote.Tests"" = json
                    }
                }
            }";

        protected AbstractSerializationTransportInformationSpec(Config config, ITestOutputHelper helper = null)
            : base(config.WithFallback(Config), output: helper)
        {
            System2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
        }

        public int Port => Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress.Port.Value;

        public string SysName => Sys.Name;

        public string Protocol => "akka.tcp";

        public ActorSystem System2 { get; }

        public Address System2Address => RARP.For(System2).Provider.DefaultAddress;

        [Fact]
        public void Serialization_of_ActorRef_in_remote_message_must_resolve_Address()
        {
            System2.ActorOf(act =>
            {
                act.ReceiveAny((o, context) => context.Sender.Tell(o));
            }, "echo");

            var echoSel = Sys.ActorSelection(new RootActorPath(System2Address) / "user" / "echo");
            echoSel.Tell(new Identify(1));
            var echo = ExpectMsg<ActorIdentity>().Subject;

            echo.Tell(new TestMessage(TestActor, echo));
            var t1 = ExpectMsg<TestMessage>();
            t1.From.Should().Be(TestActor);
            t1.To.Should().Be(echo);

            echo.Tell(new JsonSerTestMessage(TestActor, echo));
            var t2 = ExpectMsg<JsonSerTestMessage>();
            t2.From.Should().Be(TestActor);
            t2.To.Should().Be(echo);

            echo.Tell(TestActor);
            ExpectMsg(TestActor);

            echo.Tell(echo);
            ExpectMsg(echo);
        }

        protected override void AfterAll()
        {
            Shutdown(System2, verifySystemShutdown: true);
        }
    }

    public class SerializationTransportInformationSpec : AbstractSerializationTransportInformationSpec
    {
        public static readonly Config DotNettyConfig = @"akka.remote.dot-netty.tcp{
                hostname = localhost
                port = 0
            }";

        public SerializationTransportInformationSpec(ITestOutputHelper helper) : base(DotNettyConfig, helper)
        {
        }
    }
}
