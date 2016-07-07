//-----------------------------------------------------------------------
// <copyright file="OverflowStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Akka.Streams
{
    /// <summary>
    /// Represents a strategy that decides how to deal with a buffer that is full but is about to receive a new element.
    /// </summary>
    public enum OverflowStrategy
    {
        /// <summary>
        /// If the buffer is full when a new element arrives, drops the oldest element from the buffer to make space for the new element.
        /// </summary>
        DropHead = 2,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops the youngest element from the buffer to make space for the new element.
        /// </summary>
        DropTail,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops all the buffered elements to make space for the new element.
        /// </summary>
        DropBuffer,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops the new element.
        /// </summary>
        DropNew,

        /// <summary>
        /// If the buffer is full when a new element is available this strategy backpressures the upstream publisher until space becomes available in the buffer.
        /// </summary>
        Backpressure,

        /// <summary>
        /// If the buffer is full when a new element is available this strategy completes the stream with failure.
        /// </summary>
        Fail
    }

    public enum DelayOverflowStrategy
    {
        /// <summary>
        /// If the buffer is full when a new element is available this strategy send next element downstream without waiting
        /// </summary>
        EmitEarly = 1,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops the oldest element from the buffer to make space for the new element.
        /// </summary>
        DropHead,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops the youngest element from the buffer to make space for the new element.
        /// </summary>
        DropTail,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops all the buffered elements to make space for the new element.
        /// </summary>
        DropBuffer,

        /// <summary>
        /// If the buffer is full when a new element arrives, drops the new element.
        /// </summary>
        DropNew,

        /// <summary>
        /// If the buffer is full when a new element is available this strategy backpressures the upstream publisher until space becomes available in the buffer.
        /// </summary>
        Backpressure,

        /// <summary>
        /// If the buffer is full when a new element is available this strategy completes the stream with failure.
        /// </summary>
        Fail
    }

    [Serializable]
    public class BufferOverflowException : Exception
    {
        public BufferOverflowException(string message) : base(message)
        {
        }

        public BufferOverflowException(string message, Exception inner) : base(message, inner)
        {
        }

        protected BufferOverflowException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}