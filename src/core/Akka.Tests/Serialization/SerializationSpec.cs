using Akka.Configuration;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;
using System;
using System.Collections.Generic;
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
        public void CanSerializeAddress()
        {
            var message = new Address("abc", "def", "ghi", 123);
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeImmutableMessages()
        {
            var message = new ImmutableMessage(Tuple.Create("aaa", "bbb"));
            AssertEqual(message);        
        }

        [Fact]
        public void CanSerializeImmutableMessagesWithPrivateCtor()
        {
            var message = new ImmutableMessageWithPrivateCtor(Tuple.Create("aaa", "bbb"));
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeProps()
        {           
            var message = Props.Create<BlackHoleActor>().WithMailbox("abc").WithDispatcher("def");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeDeploy()
        {
            var message = new Deploy(RouterConfig.NoRouter).WithMailbox("abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeRemoteScope()
        {
            var message = new RemoteScope(new Address("akka.tcp", "foo", "localhost", 8080));
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeLocalScope()
        {
            var message = LocalScope.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeRoundRobinPool()
        {
            var message = new RoundRobinPool(10, new DefaultResizer(0,1));
            AssertEqual(message);
        }

        [Fact(Skip = "What am I doing wrong??")]
        public void CanSerializeRoundRobinGroup()
        {
            var message = new RoundRobinGroup("abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeRandomPool()
        {
            var message = new RandomPool(10, new DefaultResizer(0, 1));
            AssertEqual(message);
        }

        [Fact(Skip = "What am I doing wrong??")]
        public void CanSerializeRandomGroup()
        {
            var message = new RandomGroup("abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeConsistentHashPool()
        {
            var message = new ConsistentHashingPool(10);
            AssertEqual(message);
        }


        [Fact]
        public void CanSerializeTailChoppingPool()
        {            
            var message = new TailChoppingPool(10,TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(2));
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeScatterGatherFirstCompletedPool()
        {
            var message = new ScatterGatherFirstCompletedPool(10);
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeSmallestMailboxPool()
        {
            var message = new SmallestMailboxPool(10);
            AssertEqual(message);
        }



        [Fact]
        public void CanSerializeResizer()
        {
            var message = new DefaultResizer(1, 20);
            AssertEqual(message);
        }

        private void AssertEqual<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (T)serializer.FromBinary(serialized, typeof(T));

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
            AssertEqual(aref);
        }

        [Fact]
        public void CanSerializeDecider()
        {
            var decider = Decider.From(
                Directive.Restart,
                Directive.Stop.When<ArgumentException>(),
                Directive.Stop.When<NullReferenceException>());

            var serializer = Sys.Serialization.FindSerializerFor(decider);
            var bytes = serializer.ToBinary(decider);
            var sref = (DeployableDecider)serializer.FromBinary(bytes, typeof(DeployableDecider));
            Assert.NotNull(sref);
            Assert.Equal(decider.Pairs[0],sref.Pairs[0]);
            Assert.Equal(decider.Pairs[1], sref.Pairs[1]);
            Assert.Equal(decider.DefaultDirective, sref.DefaultDirective);            
        }

        [Fact]
        public void CanSerializeSupervisor()
        {
            var decider = Decider.From(
                Directive.Restart,
                Directive.Stop.When<ArgumentException>(),
                Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var serializer = Sys.Serialization.FindSerializerFor(supervisor);
            var bytes = serializer.ToBinary(supervisor);
            var sref = (OneForOneStrategy)serializer.FromBinary(bytes, typeof(OneForOneStrategy));
            Assert.NotNull(sref);
            var sdecider = sref.Decider as DeployableDecider;
            Assert.Equal(decider.Pairs[0], sdecider.Pairs[0]);
            Assert.Equal(decider.Pairs[1], sdecider.Pairs[1]);
            Assert.Equal(supervisor.MaxNumberOfRetries, sref.MaxNumberOfRetries);
            Assert.Equal(supervisor.WithinTimeRangeMilliseconds, sref.WithinTimeRangeMilliseconds);
            Assert.Equal(decider.DefaultDirective, sdecider.DefaultDirective);
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
