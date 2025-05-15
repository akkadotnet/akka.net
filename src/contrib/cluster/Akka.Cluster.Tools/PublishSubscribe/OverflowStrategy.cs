// -----------------------------------------------------------------------
//  <copyright file="OverflowStrategy.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.Cluster.Tools.PublishSubscribe;

/// <summary>
/// Represents a strategy that decides how to deal with a buffer that is full but is about to receive a new message.
/// </summary>
public enum OverflowStrategy
{
    /// <summary>
    /// If the buffer is full when a new message arrives, drops the oldest message from the buffer to make space for the new message.
    /// </summary>
    DropHead,

    /// <summary>
    /// If the buffer is full when a new message arrives, drops the youngest message from the buffer to make space for the new message.
    /// </summary>
    DropTail,

    /// <summary>
    /// If the buffer is full when a new message arrives, drops all the buffered messages to make space for the new message.
    /// </summary>
    DropBuffer,

    /// <summary>
    /// If the buffer is full when a new message arrives, drops the new message.
    /// </summary>
    DropNew,

    /// <summary>
    /// If the buffer is full when a new message is available this strategy throws an exception.
    /// </summary>
    Fail
}
