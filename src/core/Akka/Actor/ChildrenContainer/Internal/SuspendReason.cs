//-----------------------------------------------------------------------
// <copyright file="SuspendReason.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            private readonly Exception _cause;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public Recreation(Exception cause)
            {
                _cause = cause;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Exception Cause { get { return _cause; } }
        }

        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Termination : SuspendReason
        {
            private static readonly Termination _instance = new Termination();
            private Termination() { }
            /// <summary>
            /// TBD
            /// </summary>
            public static Termination Instance { get { return _instance; } }
        }

        /// <summary>
        /// TBD
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class UserRequest : SuspendReason
        {
            private static readonly UserRequest _instance = new UserRequest();
            private UserRequest() { }
            /// <summary>
            /// TBD
            /// </summary>
            public static UserRequest Instance { get { return _instance; } }
        }
    }
}

