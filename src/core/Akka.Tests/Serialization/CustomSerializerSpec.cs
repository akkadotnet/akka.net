//-----------------------------------------------------------------------
// <copyright file="CustomSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Serialization;
using Akka.TestKit;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Xunit;
using FluentAssertions;

namespace Akka.Tests.Serialization
{
    public class CustomSerializerSpec
    {
        /// <summary>
        /// Here we basically verify that a serializer decides where its Serializer Identifier is coming
        /// from. When using the default Serializer base class, it read from hocon config. But this should not be 
        /// a neccesity
        /// </summary>
        [Fact]
        public void Custom_serializer_must_be_owner_of_its_serializerId()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers {
                        custom = ""Akka.Tests.Serialization.CustomSerializer, Akka.Tests""
                    }
                    serialization-bindings {
                        ""System.Object"" = custom
                    }
                }
            ");
            //The above config explictly does not configures the serialization-identifiers section
            using (var system = ActorSystem.Create(nameof(CustomSerializerSpec), config))
            {
                var serializer = (CustomSerializer)system.Serialization.FindSerializerForType(typeof(object));
                Assert.Equal(666, serializer.Identifier);
            }
        }

        [Fact]
        public void Custom_SerializerWithStringManifest_should_work_with_base_class_binding()
        {
            var config = ConfigurationFactory.ParseString(@"
                akka.actor {
                    serializers {
                        custom = ""Akka.Tests.Serialization.CustomManifestSerializer, Akka.Tests""
                    }
                    serialization-bindings {
                        ""Akka.Tests.Serialization.MessageBase, Akka.Tests"" = custom
                    }
                    serialization-identifiers {
	                    ""Akka.Tests.Serialization.CustomManifestSerializer, Akka.Tests"" = 101
                    }
                }
            ");
            
            using (var system = ActorSystem.Create(nameof(CustomSerializerSpec), config))
            {
                var firstMessage = new FirstMessage("First message");
                var serialization = system.Serialization;
                var serializer = (CustomManifestSerializer)serialization.FindSerializerFor(firstMessage);

                var serialized = serializer.ToBinary(firstMessage);
                var manifest = serializer.Manifest(firstMessage);
                var deserializedFirstMessage = serializer.FromBinary(serialized, manifest);
                manifest.Should().Be(FirstMessage.Manifest);
                deserializedFirstMessage.Should().Be(firstMessage);

                var secondMessage = new SecondMessage("Second message");
                serialized = serializer.ToBinary(secondMessage);
                manifest = serializer.Manifest(secondMessage);
                var deserializedSecondMessage = serializer.FromBinary(serialized, manifest);
                manifest.Should().Be(SecondMessage.Manifest);
                deserializedSecondMessage.Should().Be(secondMessage);
            }
        }
        
        [Fact]
        public void Custom_programmatic_SerializerWithStringManifest_should_work_with_base_class_binding()
        {
            var settings = SerializationSetup.Create(system =>
                ImmutableHashSet<SerializerDetails>.Empty.Add(
                    new SerializerDetails(
                        alias: "custom", 
                        serializer: new CustomManifestSerializer(system), 
                        useFor: ImmutableHashSet<Type>.Empty.Add(typeof(MessageBase))))
            );

            var setup = ActorSystemSetup.Create(settings);
            
            using (var system = ActorSystem.Create(nameof(CustomSerializerSpec), setup))
            {
                var firstMessage = new FirstMessage("First message");
                var serialization = system.Serialization;
                var serializer = (CustomManifestSerializer)serialization.FindSerializerFor(firstMessage);

                var serialized = serializer.ToBinary(firstMessage);
                var manifest = serializer.Manifest(firstMessage);
                var deserializedFirstMessage = serializer.FromBinary(serialized, manifest);
                manifest.Should().Be(FirstMessage.Manifest);
                deserializedFirstMessage.Should().Be(firstMessage);

                var secondMessage = new SecondMessage("Second message");
                serialized = serializer.ToBinary(secondMessage);
                manifest = serializer.Manifest(secondMessage);
                var deserializedSecondMessage = serializer.FromBinary(serialized, manifest);
                manifest.Should().Be(SecondMessage.Manifest);
                deserializedSecondMessage.Should().Be(secondMessage);
            }
        }        
    }

    internal abstract class MessageBase: IEquatable<MessageBase>
    {
        public abstract string Message { get; }

        public bool Equals(MessageBase other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Message == other.Message;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is MessageBase msg && Equals(msg);
        }

        public override int GetHashCode()
        {
            return (Message != null ? Message.GetHashCode() : 0);
        }
    }

    internal class FirstMessage : MessageBase
    {
        public const string Manifest = "FM";
            
        public FirstMessage(string message)
        {
            Message = message;
        }

        public override string Message { get; }
    }
        
    internal class SecondMessage : MessageBase
    {
        public const string Manifest = "SM";
            
        public SecondMessage(string message)
        {
            Message = message;
        }

        public override string Message { get; }
    }
    
    internal class CustomManifestSerializer : SerializerWithStringManifest
    {
        public CustomManifestSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 101;

        public override byte[] ToBinary(object obj)
            => Encoding.UTF8.GetBytes(((MessageBase)obj).Message);

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                    case FirstMessage.Manifest:
                        return new FirstMessage(Encoding.UTF8.GetString(bytes));
                    case SecondMessage.Manifest:
                        return new SecondMessage(Encoding.UTF8.GetString(bytes));
                    default:
                        throw new Exception($"Unknown manifest [{manifest}]");
            }
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case FirstMessage _ :
                    return FirstMessage.Manifest;
                case SecondMessage _ :
                    return SecondMessage.Manifest;
                default:
                    throw new Exception($"Unknown object type {o.GetType()}");
            }
        }
    }
    
    public class CustomSerializer : Serializer
    {
        public CustomSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// This custom serializer overrides the Identifier implementation and returns a hard coded value
        /// </summary>
        public override int Identifier => 666;

        public override bool IncludeManifest => false;

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
