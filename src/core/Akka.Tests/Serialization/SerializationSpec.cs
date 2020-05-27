//-----------------------------------------------------------------------
// <copyright file="SerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Routing;
using Akka.Serialization;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util;
using Akka.Util.Reflection;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Serialization
{
    
    public class SerializationSpec : AkkaSpec
    {
        public class UntypedContainerMessage : IEquatable<UntypedContainerMessage>
        {
            public bool Equals(UntypedContainerMessage other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Equals(Contents, other.Contents);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((UntypedContainerMessage) obj);
            }

            public override int GetHashCode()
            {
                return (Contents != null ? Contents.GetHashCode() : 0);
            }

            public static bool operator ==(UntypedContainerMessage left, UntypedContainerMessage right)
            {
                return Equals(left, right);
            }

            public static bool operator !=(UntypedContainerMessage left, UntypedContainerMessage right)
            {
                return !Equals(left, right);
            }

            public object Contents { get; set; }

            public override string ToString()
            {
                return string.Format("<UntypedContainerMessage {0}>", Contents);
            }
        }
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

            public ImmutableMessageWithPrivateCtor((string, string) nonConventionalArg)
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

            public ImmutableMessage((string,string) nonConventionalArg)
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
            public IActorRef ActorRef { get; set; }
        }

        [Fact]
        public void Can_serialize_address_message()
        {
            var message = new UntypedContainerMessage { Contents = new Address("abc","def") };
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_decimal()
        {
            var message = 123.456m;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_decimal_message()
        {
            var message = new UntypedContainerMessage { Contents = 123.456m };
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_float_message()
        {
            var message = new UntypedContainerMessage {Contents = 123.456f};
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_long_message()
        {
            var message = new UntypedContainerMessage { Contents = 123L };
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_double_message()
        {
            var message = new UntypedContainerMessage { Contents = 123.456d };
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_int_message()
        {
            var message = new UntypedContainerMessage { Contents = 123};
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_long()
        {
            var message = 123L;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_double()
        {
            var message = 123.456d;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_int()
        {
            var message = 123;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_float()
        {
            var message = 123.456f;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_address()
        {
            var message = new Address("abc", "def", "ghi", 123);
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_immutable_messages()
        {
            var message = new ImmutableMessage(("aaa", "bbb"));
            AssertEqual(message);        
        }

        [Fact]
        public void Can_serialize_immutable_messages_with_private_ctor()
        {
            var message = new ImmutableMessageWithPrivateCtor(("aaa", "bbb"));
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Props()
        {           
            var message = Props.Create<BlackHoleActor>().WithMailbox("abc").WithDispatcher("def");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Deploy()
        {
            var message = new Deploy(NoRouter.Instance).WithMailbox("abc");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RemoteScope()
        {
            var message = new RemoteScope(new Address("akka.tcp", "foo", "localhost", 8080));
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_LocalScope()
        {
            var message = LocalScope.Instance;
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RoundRobinPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new RoundRobinPool(10, new DefaultResizer(0,1),supervisor,"abc");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RoundRobinGroup()
        {
            var message = new RoundRobinGroup("abc");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RandomPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new RandomPool(10, new DefaultResizer(0, 1),supervisor,"abc");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_RandomGroup()
        {
            var message = new RandomGroup("abc");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_ConsistentHashPool()
        {
            var decider = Decider.From(
               Directive.Restart,
               Directive.Stop.When<ArgumentException>(),
               Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new ConsistentHashingPool(10,null,supervisor,"abc");
            AssertEqual(message);
        }


        [Fact]
        public void Can_serialize_TailChoppingPool()
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
        public void Can_serialize_ScatterGatherFirstCompletedPool()
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
        public void Can_serialize_SmallestMailboxPool()
        {
            var decider = Decider.From(
             Directive.Restart,
             Directive.Stop.When<ArgumentException>(),
             Directive.Stop.When<NullReferenceException>());

            var supervisor = new OneForOneStrategy(decider);

            var message = new SmallestMailboxPool(10,null,supervisor,"abc");
            AssertEqual(message);
        }

        [Fact]
        public void Can_serialize_Resizer()
        {
            var message = new DefaultResizer(1, 20);
            AssertEqual(message);
        }

        private void AssertEqual<T>(T message)
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var result = serializer.FromBinary(serialized, typeof(T));
            var deserialized = (T) result;

       //     Assert.True(message.Equals(deserialized));
            Assert.Equal(message, deserialized);
        }


        [Fact]
        public void Can_serialize_Config()
        {
            var message = ConfigurationFactory.Empty;
            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (Config)serializer.FromBinary(serialized, typeof(Config));

            var config1 = message.ToString();
            var config2 = deserialized.ToString();

            Assert.Equal(config1, config2);
        }


        [Fact]
        public void Can_serialize_ActorRef()
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
        public void Can_serialize_ActorPath()
        {
            var uri = "akka.tcp://sys@localhost:9000/user/actor";
            var actorPath = ActorPath.Parse(uri);

            AssertEqual(actorPath);
        }

        [Fact]
        public void Can_serialize_ActorPathContainer()
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
        public void Can_serialize_singleton_messages()
        {
            var message = PoisonPill.Instance;

            var serializer = Sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (PoisonPill)serializer.FromBinary(serialized, typeof(PoisonPill));

            Assert.NotNull(deserialized);
        }

        [Fact]
        public void Can_translate_ActorRef_from_surrogate_type()
        {
            var aref = ActorOf<BlackHoleActor>();
            AssertEqual(aref);
        }

        [Fact]
        public void Can_serialize_Decider()
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
        public void Can_serialize_Supervisor()
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

        [Fact]
        public void Can_serialize_FutureActorRef()
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
        public void Can_get_serializer_by_binding()
        {
            Sys.Serialization.FindSerializerFor(null).GetType().ShouldBe(typeof(NullSerializer));
            Sys.Serialization.FindSerializerFor(new byte[]{1,2,3}).GetType().ShouldBe(typeof(ByteArraySerializer));
            Sys.Serialization.FindSerializerFor("dummy").GetType().ShouldBe(typeof(DummySerializer));
            Sys.Serialization.FindSerializerFor(123).GetType().ShouldBe(typeof(NewtonSoftJsonSerializer));
        }

        [Fact]
        public void Can_apply_a_config_based_serializer_by_the_binding()
        {
            var dummy = (DummySerializer)Sys.Serialization.FindSerializerFor("dummy");
            dummy.Config.ShouldBe(null);

            var dummy2 = (DummyConfigurableSerializer) Sys.Serialization.GetSerializerById(-7);
            dummy2.Config.ShouldNotBe(null);
            dummy2.Config.GetString("test-key", null).ShouldBe("test value");
        }


        private static string LegacyTypeQualifiedName(Type type)
        {
            string coreAssemblyName = typeof(object).GetTypeInfo().Assembly.GetName().Name;
            var assemblyName = type.GetTypeInfo().Assembly.GetName().Name;
            var shortened = assemblyName.Equals(coreAssemblyName)
                ? type.GetTypeInfo().FullName
                : $"{type.GetTypeInfo().FullName}, {assemblyName}";
            return shortened;
        }

        [Fact]
        public void Legacy_and_shortened_types_names_are_equivalent()
        {
            var targetType = typeof(ParentClass<OtherClassA, OtherClassB, OtherClassC>.ChildClass);

            var legacyTypeManifest = LegacyTypeQualifiedName(targetType);
            var newTypeManifest = targetType.TypeQualifiedName();

            TypeCache.GetType(legacyTypeManifest).ShouldBeSame(TypeCache.GetType(newTypeManifest));
            Type.GetType(legacyTypeManifest).ShouldBeSame(Type.GetType(newTypeManifest));
        }

        public SerializationSpec():base(GetConfig())
        {
        }

        private static string GetConfig() => @"
            akka.actor {
                serializers {
                    dummy = """ + typeof(DummySerializer).AssemblyQualifiedName + @"""
                    dummy2 = """ + typeof(DummyConfigurableSerializer).AssemblyQualifiedName + @"""
                }

                serialization-bindings {
                  ""System.String"" = dummy
                }
                serialization-settings {
                    dummy2 {
                        test-key = ""test value""
                    }
                }
            }";

        public class DummySerializer : Serializer
        {
            public readonly Config Config;

            public DummySerializer(ExtendedActorSystem system) 
                : base(system)
            {
            }

            public DummySerializer(ExtendedActorSystem system, Config config) 
                : base(system)
            {
                Config = config;
            }

            public override int Identifier => -6;

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

        public class DummyConfigurableSerializer : Serializer
        {
            public readonly Config Config;

            public DummyConfigurableSerializer(ExtendedActorSystem system)
                : base(system)
            {
            }

            public DummyConfigurableSerializer(ExtendedActorSystem system, Config config)
                : base(system)
            {
                Config = config;
            }

            public override int Identifier => -7;

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

        public sealed class OtherClassA { }

        public sealed class OtherClassB { }

        public sealed class OtherClassC { }

        public sealed class ParentClass<T1, T2, T3>
        {
            public sealed class ChildClass
            {
                public string Value { get; set; }
            }
        }
    }
}

