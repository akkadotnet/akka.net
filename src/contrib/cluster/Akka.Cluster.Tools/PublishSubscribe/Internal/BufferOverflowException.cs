// -----------------------------------------------------------------------
//  <copyright file="BufferOverflowException.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.Cluster.Tools.PublishSubscribe.Internal;

public class BufferOverflowException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BufferOverflowException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public BufferOverflowException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferOverflowException"/> class.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public BufferOverflowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
