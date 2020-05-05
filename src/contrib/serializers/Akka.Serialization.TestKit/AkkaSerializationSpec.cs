//-----------------------------------------------------------------------
// <copyright file="AkkaSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Routing;
using Akka.TestKit.TestActors;
using Akka.Util;
using Xunit;

namespace Akka.Tests.Serialization
{
    public class Poco : ISurrogated
    {
        public class Surrogate : ISurrogate
        {
            public string TheName { get; set; }
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return new Poco()
                {
                    Name = TheName
                };
            }
        }
        public string Name { get; set; }
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new Surrogate
            {
                TheName = Name
            };
        }
    }
    public abstract class AkkaSerializationSpec : TestKit.Xunit2.TestKit
    {
        protected AkkaSerializationSpec(Type serializerType):base(GetConfig(serializerType))
        {
        }

        private static string GetConfig(Type serializerType)
        {
            return @"

akka.actor {
    serializers {
        testserializer = """ + serializerType.AssemblyQualifiedName + @"""
    }

    serialization-bindings {
      ""System.Object"" = testserializer
    }
}
";
        }

        [Fact]
        public void CanSerializeArrayOfTypes()
        {
            var message = new[] {typeof(NullReferenceException), typeof(ArgumentException)};
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);
            var res = (Type[])serializer.FromBinary(bytes, typeof(Type[]));
        }

        [Fact]
        public void CanSerializeSurrogate()
        {
            var message = new Poco
            {
                Name = "Foo"
            };
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var bytes = serializer.ToBinary(message);
            var res = (Poco)serializer.FromBinary(bytes, typeof(Poco));

            Assert.Equal(message.Name,res.Name);
        }
        [Fact]
        public void CanSerializeAddressMessage()
        {
            var message = new UntypedContainerMessage { Contents = new Address("abc", "def") };
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeDecimal()
        {
            var message = 123.456m;
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeDecimalMessage()
        {
            var message = new UntypedContainerMessage { Contents = 123.456m };
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeFloatMessage()
        {
            var message = new UntypedContainerMessage { Contents = 123.456f };
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeLongMessage()
        {
            var message = new UntypedContainerMessage { Contents = 123L };
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeDoubleMessage()
        {
            var message = new UntypedContainerMessage { Contents = 123.456d };
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeIntMessage()
        {
            var message = new UntypedContainerMessage { Contents = 123 };
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeLong()
        {
            var message = 123L;
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeDouble()
        {
            var message = 123.456d;
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeInt()
        {
            var message = 123;
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeFloat()
        {
            var message = 123.456f;
            AssertEqual(message);
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
            var message = new Deploy(NoRouter.Instance).WithMailbox("abc");
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
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new RoundRobinPool(10, new DefaultResizer(0, 1), supervisor, "abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeRoundRobinGroup()
        {
            var message = new RoundRobinGroup("abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeRandomPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new RandomPool(10, new DefaultResizer(0, 1), supervisor, "abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeRandomGroup()
        {
            var message = new RandomGroup("abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeConsistentHashPool()
        {
            var decider = Decider.From(
               Directive.Restart,
               Directive.Stop.When<ArgumentException>(),
               Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new ConsistentHashingPool(10, null, supervisor, "abc");
            AssertEqual(message);
        }


        [Fact]
        public void CanSerializeTailChoppingPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new TailChoppingPool(10, null, supervisor, "abc", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeScatterGatherFirstCompletedPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new ScatterGatherFirstCompletedPool(10, null, TimeSpan.MaxValue, supervisor, "abc");
            AssertEqual(message);
        }

        [Fact]
        public void CanSerializeSmallestMailboxPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new SmallestMailboxPool(10, null, supervisor, "abc");
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
            var result = serializer.FromBinary(serialized, typeof(T));
            var deserialized = (T)result;

            //     Assert.True(message.Equals(deserialized));
            Assert.Equal(message, deserialized);
        }


        [Fact]
        public void CanSerializeConfig()
        {
            var message = ConfigurationFactory.ParseString(@"
my-settings{
    a: 1
    b: 2
    c: 3
}");
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

            AssertEqual(actorPath);
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
            var message = PoisonPill.Instance;

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (PoisonPill)serializer.FromBinary(serialized, typeof(PoisonPill));

            Assert.NotNull(deserialized);
        }

        [Fact]
        public void CanTranslateActorRefFromSurrogateType()
        {
            var aref = ActorOf<BlackHoleActor>();
            AssertEqual(aref);
        }

        [Fact]
        public void CanSerializeActorRefWithUID()
        {
            var aref = ActorOf<BlackHoleActor>();
            var surrogate = aref.ToSurrogate(Sys) as ActorRefBase.Surrogate;
            var uid = aref.Path.Uid;
            Assert.Contains("#" + uid, surrogate.Path);
        }

        [Fact]
        public void CanSerializeEmptyDecider()
        {
            var decider = Decider.From(
                Directive.Restart,
                Directive.Stop.When<NullReferenceException>(),
                Directive.Escalate.When<Exception>()
                );

            var serializer = Sys.Serialization.FindSerializerFor(decider);
            var bytes = serializer.ToBinary(decider);
            var sref = (DeployableDecider)serializer.FromBinary(bytes, typeof(DeployableDecider));
            Assert.NotNull(sref);
            Assert.Equal(decider.DefaultDirective, sref.DefaultDirective);
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
            Assert.Equal(decider.Pairs[0], sref.Pairs[0]);
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

        [Fact]
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
    }
}

