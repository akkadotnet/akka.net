//-----------------------------------------------------------------------
// <copyright file="SuspendReason.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// TBD
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public abstract class SuspendReason
    {
        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public interface IWaitingForChildren
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Creation : SuspendReason, IWaitingForChildren
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Recreation : SuspendReason, IWaitingForChildren
        {

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public Recreation(Exception cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Exception Cause { get; }
        }

        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Termination : SuspendReason
        {
            private Termination() { }
            /// <summary>
            /// TBD
            /// </summary>
            public static Termination Instance { get; } = new Termination();
        }

        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class UserRequest : SuspendReason
        {
            private UserRequest() { }
            /// <summary>
            /// TBD
            /// </summary>
            public static UserRequest Instance { get; } = new UserRequest();
        }
    }
}

