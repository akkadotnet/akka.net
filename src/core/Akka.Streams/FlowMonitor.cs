//-----------------------------------------------------------------------
// <copyright file="FlowMonitor.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        FlowMonitor.IStreamState State { get; }
    }

    public static class FlowMonitor
    {
        public interface IStreamState
        {
            
        }

        /// <summary>
        /// Stream was created, but no events have passed through it
        /// </summary>
        public class Initialized : IStreamState
        {
            public static Initialized Instance { get; } = new Initialized();

            private Initialized()
            {
                
            }
        }

        /// <summary>
        /// Stream processed a message
        /// </summary>
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
            public static Finished Instance { get; } = new Finished();

            private Finished()
            {

            }
        }
    }
}
