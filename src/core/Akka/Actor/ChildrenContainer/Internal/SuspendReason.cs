//-----------------------------------------------------------------------
// <copyright file="SuspendReason.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Identifies why an actor cell is suspended while it processes lifecycle or user requests.
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public abstract class SuspendReason
    {
        /// <summary>
        /// Marks a suspension that waits for child actors to complete creation.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public interface IWaitingForChildren
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Marks a suspension requested while an actor is being created.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Creation : SuspendReason, IWaitingForChildren
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Marks a suspension while an actor is being recreated after failure.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Recreation : SuspendReason, IWaitingForChildren
        {

            /// <summary>
            /// Creates a recreation suspension reason with the triggering failure.
            /// </summary>
            /// <param name="cause">The exception that caused the actor to be recreated.</param>
            public Recreation(Exception cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// The exception that caused the actor to be recreated.
            /// </summary>
            public Exception Cause { get; }
        }

        /// <summary>
        /// Marks a suspension while an actor is terminating.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Termination : SuspendReason
        {
            private Termination() { }
            /// <summary>
            /// The shared termination suspension reason.
            /// </summary>
            public static Termination Instance { get; } = new();
        }

        /// <summary>
        /// Marks a suspension requested explicitly by a user or parent.
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class UserRequest : SuspendReason
        {
            private UserRequest() { }
            /// <summary>
            /// The shared user-request suspension reason.
            /// </summary>
            public static UserRequest Instance { get; } = new();
        }
    }
}
