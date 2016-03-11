//-----------------------------------------------------------------------
// <copyright file="TestSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Serialization;

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
            if (obj is MyPayload)
                return Encoding.UTF8.GetBytes("." + ((MyPayload) obj).Data);
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

    public class MyPayload2Serializer : SerializerWithStringManifest
    {
        private readonly string _manifestV1 = TypeQualifiedNameForManifest(typeof (MyPayload));
        private readonly string _manifestV2 = "MyPayload-V2";

        public MyPayload2Serializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 77125; }
        }

        public override string Manifest(object o)
        {
            return _manifestV2;
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is MyPayload2)
                return Encoding.UTF8.GetBytes(string.Format(".{0}:{1}", ((MyPayload2) obj).Data, ((MyPayload2) obj).N));
            return null;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest.Equals(_manifestV2))
            {
                var parts = Encoding.UTF8.GetString(bytes).Split(':');
                return new MyPayload2(parts[0] + ".", int.Parse(parts[1]));
            }
            if (manifest.Equals(_manifestV1))
                return new MyPayload2(Encoding.UTF8.GetString(bytes) + ".", 0);
            throw new ArgumentException("unexpected manifest " + manifest);
        }
    }

    public class MySnapshotSerializer : Serializer
    {
        public MySnapshotSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 77124; }
        }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is MySnapshot)
                return Encoding.UTF8.GetBytes("." + ((MySnapshot) obj).Data);
            return null;
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == null)
                throw new ArgumentException("no manifest");
            if (type == typeof (MySnapshot))
                return new MySnapshot(string.Format("{0}.", Encoding.UTF8.GetString(bytes)));
            throw new ArgumentException("unexpected manifest " + type);
        }
    }

    public class MySnapshotSerializer2 : SerializerWithStringManifest
    {
        private readonly string _oldManifest = TypeQualifiedNameForManifest(typeof (MySnapshot));
        private readonly string _currentManifest = "MySnapshot-V2";

        public MySnapshotSerializer2(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 77126; }
        }

        public override string Manifest(object o)
        {
            return _currentManifest;
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is MySnapshot2)
                return Encoding.UTF8.GetBytes(string.Format(".{0}", ((MySnapshot2) obj).Data));
            return null;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest.Equals(_currentManifest) || manifest.Equals(_oldManifest))
                return new MySnapshot2(Encoding.UTF8.GetString(bytes) + ".");
            throw new ArgumentException("unexpected manifest " + manifest);
        }
    }

    public class OldPayloadSerializer : SerializerWithStringManifest
    {
        private readonly string _oldPayloadTypeName = "Akka.Persistence.Tests.Serialization.OldPayload,Akka.Persistence.Tests";
        private readonly string _myPayloadTypeName = TypeQualifiedNameForManifest(typeof(MyPayload));

        public OldPayloadSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier
        {
            get { return 77127; }
        }

        public override string Manifest(object o)
        {
            return TypeQualifiedNameForManifest(o.GetType());
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is MyPayload)
                return Encoding.UTF8.GetBytes(string.Format(".{0}", ((MyPayload) obj).Data));
            if (TypeQualifiedNameForManifest(obj.GetType()).Equals(_oldPayloadTypeName))
                return Encoding.UTF8.GetBytes(obj.ToString());
            return null;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (manifest.Equals(_oldPayloadTypeName))
                return new MyPayload(Encoding.UTF8.GetString(bytes));
            if (manifest.Equals(_myPayloadTypeName))
                return new MyPayload(Encoding.UTF8.GetString(bytes) + ".");
            throw new ArgumentException("unexpected manifest " + manifest);
        }
    }
}