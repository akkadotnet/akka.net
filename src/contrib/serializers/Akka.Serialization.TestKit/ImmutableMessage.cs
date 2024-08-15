﻿// -----------------------------------------------------------------------
//  <copyright file="ImmutableMessage.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.Tests.Serialization;

public class ImmutableMessage
{
    public ImmutableMessage()
    {
    }

    public ImmutableMessage(Tuple<string, string> nonConventionalArg)
    {
        Foo = nonConventionalArg.Item1;
        Bar = nonConventionalArg.Item2;
    }

    public string Foo { get; }
    public string Bar { get; }

    protected bool Equals(ImmutableMessage other)
    {
        return string.Equals(Bar, other.Bar) && string.Equals(Foo, other.Foo);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((ImmutableMessage)obj);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((Bar != null ? Bar.GetHashCode() : 0) * 397) ^ (Foo != null ? Foo.GetHashCode() : 0);
        }
    }
}