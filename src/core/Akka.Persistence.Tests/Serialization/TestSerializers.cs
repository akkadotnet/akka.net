﻿// -----------------------------------------------------------------------
//  <copyright file="TestSerializers.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Tests.Serialization;

public class MyPayloadSerializer : Serializer
{
    public MyPayloadSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => 77123;

    public override bool IncludeManifest => true;

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
        if (type == typeof(MyPayload))
            return new MyPayload(string.Format("{0}.", Encoding.UTF8.GetString(bytes)));
        throw new ArgumentException("unexpected manifest " + type);
    }
}