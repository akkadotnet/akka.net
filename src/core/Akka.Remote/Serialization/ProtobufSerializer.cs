﻿//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Remote.Serialization
{
    public class ProtobufSerializer : Serializer
    {
        public ProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        public override int Identifier
        {
            get { return 2; }
        }

        public override byte[] ToBinary(object obj)
        {
            throw new NotImplementedException();
            //using (var stream = new MemoryStream())
            //{
            //    global::ProtoBuf.Serializer.Serialize(stream, obj);
            //    return stream.ToArray();
            //}
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
            //using (var stream = new MemoryStream(bytes))
            //{
            //    return global::ProtoBuf.Serializer.NonGeneric.Deserialize(type, stream);
            //}
        }
    }
}

