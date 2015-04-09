//-----------------------------------------------------------------------
// <copyright file="SuspendReason.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public abstract class SuspendReason
    {
        /// <summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        // ReSharper disable once InconsistentNaming
        public interface IWaitingForChildren
        {
            //Intentionally left blank
        }

        /// <summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Creation : SuspendReason, IWaitingForChildren
        {
            //Intentionally left blank
        }

        /// <summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Recreation : SuspendReason, IWaitingForChildren
        {
            private readonly Exception _cause;

            public Recreation(Exception cause)
            {
                _cause = cause;
            }

            public Exception Cause { get { return _cause; } }
        }

        /// <summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class Termination : SuspendReason
        {
            private static readonly Termination _instance = new Termination();
            private Termination() { }
            public static Termination Instance { get { return _instance; } }
        }

        /// <summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public class UserRequest : SuspendReason
        {
            private static readonly UserRequest _instance = new UserRequest();
            private UserRequest() { }
            public static UserRequest Instance { get { return _instance; } }
        }
    }
}

