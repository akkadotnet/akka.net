﻿// -----------------------------------------------------------------------
//  <copyright file="OtherMessageComparer.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.DistributedData.Serialization.Proto.Msg;

namespace Akka.DistributedData.Serialization;

internal class OtherMessageComparer : IComparer<OtherMessage>
{
    private OtherMessageComparer()
    {
    }

    public static OtherMessageComparer Instance { get; } = new();

    public int Compare(OtherMessage a, OtherMessage b)
    {
        if (a == null || b == null)
            throw new Exception("Both messages must not be null");
        if (ReferenceEquals(a, b)) return 0;

        var aByteString = a.EnclosedMessage.Span;
        var bByteString = b.EnclosedMessage.Span;
        var aSize = aByteString.Length;
        var bSize = bByteString.Length;
        if (aSize < bSize) return -1;
        if (aSize > bSize) return 1;

        for (var i = 0; i < aSize; i++)
        {
            var aByte = aByteString[i];
            var bByte = bByteString[i];
            if (aByte < bByte) return -1;
            if (aByte > bByte) return 1;
        }

        return 0;
    }
}