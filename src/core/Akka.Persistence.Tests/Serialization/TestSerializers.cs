//-----------------------------------------------------------------------
// <copyright file="TestSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
}
