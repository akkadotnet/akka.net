//-----------------------------------------------------------------------
// <copyright file="TestSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.TCK.Serialization
{
    public sealed class TestPayload
    {
        public TestPayload(IActorRef @ref)
        {
            Ref = @ref;
        }

        public IActorRef Ref { get; }

        private bool Equals(TestPayload other)
        {
            return Ref.Equals(other.Ref);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TestPayload other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Ref.GetHashCode();
        }

        public static bool operator ==(TestPayload left, TestPayload right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(TestPayload left, TestPayload right)
        {
            return !Equals(left, right);
        }
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
                case TestPayload p:
                    VerifyTransportInfo();
                    var refStr = Akka.Serialization.Serialization.SerializedActorPath(p.Ref);
                    return Encoding.UTF8.GetBytes(refStr);
                default:
                    throw new SerializationException($"Unsupported message type [{obj?.GetType()}]");
            }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            VerifyTransportInfo();
            switch (manifest)
            {
                case "A":
                    var refStr = Encoding.UTF8.GetString(bytes);
                    var actorRef = system.Provider.ResolveActorRef(refStr);
                    return new TestPayload(actorRef);
                default:
                    throw new SerializationException($"Unsupported message type [{manifest}]");
            }
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case TestPayload tp:
                    return "A";
                default:
                    return null;
            }
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
}
