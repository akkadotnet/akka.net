using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Tests.Serialization
{
    
    public class SerializationSpec : AkkaSpec
    {
        public class EmptyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Context.System.EventStream.Publish(Sender);
            }
        }
        public class SomeMessage
        {
            public ActorRef ActorRef { get; set; }
        }

        [Fact]
        public void CanSerializeActorRef()
        {
            var message = new SomeMessage
            {
                ActorRef = TestActor,
            };

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

            Assert.Same(TestActor, deserialized.ActorRef);
        }

        [Fact]
        public void CanSerializeActorPath()
        {
            var uri = "akka.tcp://sys@localhost:9000/user/actor";
            var actorPath = ActorPath.Parse(uri);

            var serializer = Sys.Serialization.FindSerializerFor(actorPath);
            var serialized = serializer.ToBinary(actorPath);
            var deserialized = (ActorPath) serializer.FromBinary(serialized, typeof (ActorPath));

            Assert.Equal(actorPath, deserialized);
        }

        [Fact]
        public void CanSerializeSingletonMessages()
        {
            var message = Terminate.Instance;

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (Terminate)serializer.FromBinary(serialized, typeof(Terminate));

            Assert.NotNull(deserialized);
        }

        [Fact]
        public void CanTranslateActorRefFromSurrogateType()
        {
            var aref = ActorOf<BlackHoleActor>();
            var serializer = Sys.Serialization.FindSerializerFor(aref);
            var bytes = serializer.ToBinary(aref);
            var sref = (ActorRef)serializer.FromBinary(bytes, typeof(ActorRef));
            Assert.NotNull(sref);
        }

        //TODO: find out why this fails on build server

        [Fact(Skip="Fails on buildserver")]
        public void CanSerializeFutureActorRef()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(object));
            var empty = Sys.ActorOf<EmptyActor>();
            empty.Ask("hello");
            var f = ExpectMsg<FutureActorRef>();


            var message = new SomeMessage
            {
                ActorRef = f,
            };

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

            Assert.Same(f, deserialized.ActorRef);
        }

        [Fact()]
        public void CanGetSerializerByBinding()
        {
            Sys.Serialization.FindSerializerFor(null).GetType().ShouldBe(typeof(NullSerializer));
            Sys.Serialization.FindSerializerFor(new byte[]{1,2,3}).GetType().ShouldBe(typeof(ByteArraySerializer));
            Sys.Serialization.FindSerializerFor("dummy").GetType().ShouldBe(typeof(DummySerializer));
            Sys.Serialization.FindSerializerFor(123).GetType().ShouldBe(typeof(NewtonSoftJsonSerializer));
        }


        public SerializationSpec():base(GetConfig())
        {
        }

        private static string GetConfig()
        {
            return @"

akka.actor {
    serializers {
        dummy = """ + typeof(DummySerializer).AssemblyQualifiedName + @"""
	}

    serialization-bindings {
      ""System.String"" = dummy
    }
}
";
        }

        public class DummySerializer : Serializer
        {
            public DummySerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier
            {
                get { return -5; }
            }

            public override bool IncludeManifest
            {
                get { throw new NotImplementedException(); }
            }

            public override byte[] ToBinary(object obj)
            {
                throw new NotImplementedException();
            }

            public override object FromBinary(byte[] bytes, Type type)
            {
                throw new NotImplementedException();
            }
        }
    }
}
