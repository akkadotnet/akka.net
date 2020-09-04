//-----------------------------------------------------------------------
// <copyright file="OverflowStrategy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

    /// <summary>
    /// Represents a strategy that decides how to deal with a buffer of time based stage that is full but is about to receive a new element.
    /// </summary>
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

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
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

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="BufferOverflowException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected BufferOverflowException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
#endif
    }
}
