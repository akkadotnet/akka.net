//-----------------------------------------------------------------------
// <copyright file="TestSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Runtime.Serialization;
using System.Text;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Tests.Serialization
{
    public class MyPayloadSerializer : Serializer
    {
        public MyPayloadSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 77123; }
        }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is MyPayload payload)
                return Encoding.UTF8.GetBytes("." + payload.Data);
            return null;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == null)
                throw new ArgumentException("no manifest");
            if (type == typeof (MyPayload))
                return new MyPayload(string.Format("{0}.", Encoding.UTF8.GetString(bytes)));
            throw new ArgumentException("unexpected manifest " + type);
        }
    }

    public sealed class MyV2Payload: IEquatable<MyV2Payload>
    {
        public MyV2Payload(string data)
        {
            Data = data;
        }

        public string Data { get; }

        public bool Equals(MyV2Payload other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Data == other.Data;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((MyV2Payload)obj);
        }

        public override int GetHashCode()
        {
            return Data.GetHashCode();
        }
    }

    public sealed class MyV2PayloadSerializer : SerializerV2
    {
        public const string PayloadManifest = "my-v2-payload";

        public MyV2PayloadSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 77124;

        public override string Manifest(object obj) => PayloadManifest;

        public override int SizeHint(object obj)
        {
            return Encoding.UTF8.GetByteCount(((MyV2Payload)obj).Data);
        }

        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var value = ((MyV2Payload)obj).Data;
            var byteCount = Encoding.UTF8.GetByteCount(value);
            var span = writer.GetSpan(byteCount);
            var written = Encoding.UTF8.GetBytes(value, span);
            writer.Advance(written);
            return written;
        }

        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            if (manifest != PayloadManifest)
                throw new SerializationException($"Unknown manifest [{manifest}]");
            return new MyV2Payload(Encoding.UTF8.GetString(bytes.ToArray()));
        }
    }
}
