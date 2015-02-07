using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using System;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Tests.Serialization
{
    
    public class SerializationSpec : AkkaSpec
    {
        public class ContainerMessage<T>
        {
            public ContainerMessage(T contents)
            {
                Contents = contents;
            }
            public T Contents { get;private set; }
        }
        public class ImmutableMessageWithPrivateCtor
        {
            public string Foo { get; private set; }
            public string Bar { get; private set; }

            protected ImmutableMessageWithPrivateCtor()
            {
            }

            protected bool Equals(ImmutableMessageWithPrivateCtor other)
            {
                return string.Equals(Bar, other.Bar) && string.Equals(Foo, other.Foo);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != GetType()) return false;
                return Equals((ImmutableMessageWithPrivateCtor) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Bar != null ? Bar.GetHashCode() : 0)*397) ^ (Foo != null ? Foo.GetHashCode() : 0);
                }
            }

            public ImmutableMessageWithPrivateCtor(Tuple<string, string> nonConventionalArg)
            {
                Foo = nonConventionalArg.Item1;
                Bar = nonConventionalArg.Item2;
            }
        }

        public class ImmutableMessage
        {
            public string Foo { get;private set; }
            public string Bar { get;private set; }

            public ImmutableMessage()
            {
                
            }

            protected bool Equals(ImmutableMessage other)
            {
                return string.Equals(Bar, other.Bar) && string.Equals(Foo, other.Foo);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != GetType()) return false;
                return Equals((ImmutableMessage) obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((Bar != null ? Bar.GetHashCode() : 0)*397) ^ (Foo != null ? Foo.GetHashCode() : 0);
                }
            }

            public ImmutableMessage(Tuple<string,string> nonConventionalArg)
            {
                Foo = nonConventionalArg.Item1;
                Bar = nonConventionalArg.Item2;
            }
        }

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
        public void CanSerializeImmutableMessages()
        {
            var message = new ImmutableMessage(Tuple.Create("aaa", "bbb"));

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (ImmutableMessage)serializer.FromBinary(serialized, typeof(ImmutableMessage));

            Assert.Equal(message,deserialized);            
        }

        [Fact]
        public void CanSerializeImmutableMessagesWithPrivateCtor()
        {
            var message = new ImmutableMessageWithPrivateCtor(Tuple.Create("aaa", "bbb"));

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (ImmutableMessageWithPrivateCtor)serializer.FromBinary(serialized, typeof(ImmutableMessageWithPrivateCtor));

            Assert.Equal(message, deserialized);
        }

        [Fact]
        public void CanSerializeProps()
        {           
            var message = Props.Create<BlackHoleActor>().WithMailbox("abc").WithDispatcher("def");
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (Props)serializer.FromBinary(serialized, typeof(Props));

            Assert.Equal(message, deserialized);
        }

        [Fact]
        public void CanSerializeDeploy()
        {
            var message = new Deploy(RouterConfig.NoRouter).WithMailbox("abc");
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (Deploy)serializer.FromBinary(serialized, typeof(Deploy));

            Assert.Equal(message, deserialized);
        }

        [Fact]
        public void CanSerializeScope()
        {
            var message = new RemoteScope(new Address("akka.tcp", "foo", "localhost", 8080));
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (RemoteScope)serializer.FromBinary(serialized, typeof(RemoteScope));

            Assert.Equal(message, deserialized);
        }

        [Fact]
        public void CanSerializePool()
        {
            var message = new RoundRobinPool(10, new DefaultResizer(0,1));
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (RoundRobinPool)serializer.FromBinary(serialized, typeof(RoundRobinPool));
            Assert.Equal(message, deserialized);
        }

        [Fact]
        public void CanSerializeResizer()
        {
            var message = new DefaultResizer(1, 20);
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (DefaultResizer)serializer.FromBinary(serialized, typeof(DefaultResizer));

            Assert.Equal(message, deserialized);
        }

        [Fact]
        public void CanSerializeConfig()
        {
            var message = ConfigurationFactory.Default();
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (Config)serializer.FromBinary(serialized, typeof(Config));

            var config1 = message.ToString();
            var config2 = deserialized.ToString();

            Assert.Equal(config1, config2);
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
            var deserialized = (ActorPath) serializer.FromBinary(serialized, typeof (object));

            Assert.Equal(actorPath, deserialized);
        }

        [Fact]
        public void CanSerializeActorPathContainer()
        {
            var uri = "akka.tcp://sys@localhost:9000/user/actor";
            var actorPath = ActorPath.Parse(uri);
            var container = new ContainerMessage<ActorPath>(actorPath);
            var serializer = Sys.Serialization.FindSerializerFor(container);
            var serialized = serializer.ToBinary(container);
            var deserialized = (ContainerMessage<ActorPath>)serializer.FromBinary(serialized, typeof(ContainerMessage<ActorPath>));

            Assert.Equal(actorPath, deserialized.Contents);
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

        [Fact]
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
