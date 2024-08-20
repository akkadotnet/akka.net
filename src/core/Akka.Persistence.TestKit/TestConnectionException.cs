// -----------------------------------------------------------------------
// <copyright file="TestJournalConnectionException.cs" company="Petabridge, LLC">
//      Copyright (C) 2015 - 2024 Petabridge, LLC <https://petabridge.com>
// </copyright>
// -----------------------------------------------------------------------

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