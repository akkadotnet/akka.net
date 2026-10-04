//-----------------------------------------------------------------------
// <copyright file="FlowMonitor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    /// <summary>
    /// Used to monitor the state of a stream
    /// </summary>
    public interface IFlowMonitor
    {
        /// <summary>
        /// The most recently observed state of the monitored stream.
        /// </summary>
        FlowMonitor.IStreamState State { get; }
    }

    /// <summary>
    /// State values reported by a monitored stream.
    /// </summary>
    public static class FlowMonitor
    {
        /// <summary>
        /// Represents a state reported by a stream monitor.
        /// </summary>
        public interface IStreamState
        {
            
        }

        /// <summary>
        /// Stream was created, but no events have passed through it
        /// </summary>
        public class Initialized : IStreamState
        {
            /// <summary>
            /// Singleton state value for a stream before it processes a message.
            /// </summary>
            public static Initialized Instance { get; } = new();

            private Initialized()
            {
                
            }
        }

        /// <summary>
        /// Stream processed a message
        /// </summary>
        /// <typeparam name="T">Type of the processed message.</typeparam>
        public sealed class Received<T> : IStreamState
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Received{T}"/> class.
            /// </summary>
            /// <param name="message">The processed message</param>
            public Received(T message)
            {
                Message = message;
            }

            /// <summary>
            /// The processed message
            /// </summary>
            public T Message { get; }
        }

        /// <summary>
        /// Stream failed
        /// </summary>
        public sealed class Failed : IStreamState
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Failed"/> class.
            /// </summary>
            /// <param name="cause">The cause of the failure</param>
            public Failed(Exception cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// The cause of the failure
            /// </summary>
            public Exception Cause { get; }
        }

        /// <summary>
        /// Stream completed successfully
        /// </summary>
        public class Finished : IStreamState
        {
            /// <summary>
            /// Singleton state value for a stream that completed successfully.
            /// </summary>
            public static Finished Instance { get; } = new();

            private Finished()
            {

            }
        }
    }
}
