// -----------------------------------------------------------------------
//  <copyright file="TestSnapshotStoreFailureException.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Persistence.TestKit;

[Serializable]
public class TestSnapshotStoreFailureException : Exception
{
    public TestSnapshotStoreFailureException()
    {
    }

    public TestSnapshotStoreFailureException(string message) : base(message)
    {
    }

    public TestSnapshotStoreFailureException(string message, Exception inner) : base(message, inner)
    {
    }

    protected TestSnapshotStoreFailureException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}