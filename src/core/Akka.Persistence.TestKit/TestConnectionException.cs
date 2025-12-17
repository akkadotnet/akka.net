//-----------------------------------------------------------------------
// <copyright file="TestConnectionException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Persistence.TestKit;

public class TestConnectionException: Exception
{
    public TestConnectionException() { }
    public TestConnectionException(string message) : base(message) { }
    public TestConnectionException(string message, Exception inner) : base(message, inner) { }
    protected TestConnectionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
