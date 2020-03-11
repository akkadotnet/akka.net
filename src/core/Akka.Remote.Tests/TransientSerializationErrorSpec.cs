//-----------------------------------------------------------------------
// <copyright file="TransientSerializationErrorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Akka.Serialization;
using System.Runtime.Serialization;

namespace Akka.Remote.Tests
{

    public abstract class AbstractTransientSerializationErrorSpec : AkkaSpec
    {
        internal class ManifestNotSerializable
        {
            public static readonly ManifestNotSerializable Instance = new ManifestNotSerializable();

            private ManifestNotSerializable() { }
        }

        internal class ManifestIllegal
        {
            public static readonly ManifestIllegal Instance = new ManifestIllegal();

            private ManifestIllegal() { }
        }

        internal class ToBinaryNotSerializable
        {
            public static readonly ToBinaryNotSerializable Instance = new ToBinaryNotSerializable();

            private ToBinaryNotSerializable() { }
        }

        internal class ToBinaryIllegal
        {
            public static readonly ToBinaryIllegal Instance = new ToBinaryIllegal();

            private ToBinaryIllegal() { }
        }

        internal class NotDeserializable
        {
            public static readonly NotDeserializable Instance = new NotDeserializable();

            private NotDeserializable() { }
        }

        internal class IllegalOnDeserialize
        {
            public static readonly IllegalOnDeserialize Instance = new IllegalOnDeserialize();

            private IllegalOnDeserialize() { }
        }

        internal class TestSerializer : SerializerWithStringManifest
        {
            public override int Identifier => 666;

            public TestSerializer(ExtendedActorSystem system)
                : base(system)
            {
            }

            public override string Manifest(object o)
            {
                switch (o)
                {
                    case ManifestNotSerializable _:
                        throw new SerializationException();
                    case ManifestIllegal _:
                        throw new ArgumentException();
                    case ToBinaryNotSerializable _:
                        return "TBNS";
                    case ToBinaryIllegal _:
                        return "TI";
                    case NotDeserializable _:
                        return "ND";
                    case IllegalOnDeserialize _:
                        return "IOD";
                }
                throw new InvalidOperationException();
            }

            public override object FromBinary(byte[] bytes, string manifest)
            {
                switch (manifest)
                {
                    case "ND":
                        throw new SerializationException();
                    case "IOD":
                        throw new ArgumentException();
                }
                throw new InvalidOperationException();
            }

            public override byte[] ToBinary(object obj)
            {
                switch (obj)
                {
                    case ToBinaryNotSerializable _:
                        throw new SerializationException();
                    case ToBinaryIllegal _:
                        throw new ArgumentException();
                    default:
                        return new byte[0];
                }
            }
        }

        internal class EchoActor : ActorBase
        {
            public static Props Props()
            {
                return Actor.Props.Create(() => new EchoActor());
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(message);
                return true;
            }
        }

        private readonly ActorSystem system2;
        private readonly Address system2Address;

        public AbstractTransientSerializationErrorSpec(Config config)
            : base(config.WithFallback(ConfigurationFactory.ParseString(GetConfig())))
        {
            var port = ((ExtendedActorSystem)Sys).Provider.DefaultAddress.Port;

            system2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            system2Address = ((ExtendedActorSystem)system2).Provider.DefaultAddress;
        }

        private static string GetConfig()
        {
            return @"
            akka {
                loglevel = info
                actor {
                    provider = remote
                    serializers {
                        test = ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+TestSerializer, Akka.Remote.Tests""
                    }
                    serialization-bindings {
                        ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+ManifestNotSerializable, Akka.Remote.Tests"" = test
                        ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+ManifestIllegal, Akka.Remote.Tests"" = test
                        ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+ToBinaryNotSerializable, Akka.Remote.Tests"" = test
                        ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+ToBinaryIllegal, Akka.Remote.Tests"" = test
                        ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+NotDeserializable, Akka.Remote.Tests"" = test
                        ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+IllegalOnDeserialize, Akka.Remote.Tests"" = test
                    }
                    #serialization-identifiers {
                    #  ""Akka.Remote.Tests.AbstractTransientSerializationErrorSpec+TestSerializer, Akka.Remote.Tests"" = 666
                    #}
                }
            }
            ";
        }

        protected override void AfterTermination()
        {
            Shutdown(system2);
        }


        [Fact]
        public void The_transport_must_stay_alive_after_a_transient_exception_from_the_serializer()
        {
            system2.ActorOf(EchoActor.Props(), "echo");

            var selection = Sys.ActorSelection(new RootActorPath(system2Address) / "user" / "echo");

            selection.Tell("ping", this.TestActor);
            ExpectMsg("ping");

            // none of these should tear down the connection
            selection.Tell(ManifestIllegal.Instance, this.TestActor);
            selection.Tell(ManifestNotSerializable.Instance, this.TestActor);
            selection.Tell(ToBinaryIllegal.Instance, this.TestActor);
            selection.Tell(ToBinaryNotSerializable.Instance, this.TestActor);
            selection.Tell(NotDeserializable.Instance, this.TestActor);
            selection.Tell(IllegalOnDeserialize.Instance, this.TestActor);

            // make sure we still have a connection
            selection.Tell("ping", this.TestActor);
            ExpectMsg("ping");
        }
    }



    public class TransientSerializationErrorSpec : AbstractTransientSerializationErrorSpec
    {
        public TransientSerializationErrorSpec()
            : base(ConfigurationFactory.ParseString(@"
                akka.remote.dot-netty.tcp {
                    hostname = localhost
                    port = 0
                }
                "))
        {
        }
    }
}

