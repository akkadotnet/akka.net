//-----------------------------------------------------------------------
// <copyright file="SuspendReason.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace Akka.Actor.Internal
{
    internal static class SuspendReasonStatus
    {
        public const int Creation = 1;
        public const int Recreation = 2;
        public const int Termination = 4;
        public const int UserRequest = 8;

        public const int WaitingForChildren = Creation | Recreation;
    }

    public struct SuspendReason
    {
        public static readonly SuspendReason Creation = new SuspendReason(SuspendReasonStatus.Creation);
        public static readonly SuspendReason Termination = new SuspendReason(SuspendReasonStatus.Termination);
        public static readonly SuspendReason UserRequest = new SuspendReason(SuspendReasonStatus.UserRequest);

        public int Status { get; }
        public Exception Cause { get; }

        private SuspendReason(int status)
        {
            Status = status;
            Cause = null;
        }

        public SuspendReason(Exception cause)
        {
            Status = SuspendReasonStatus.Recreation;
            Cause = cause;
        }

        public bool IsCreation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return (Status & SuspendReasonStatus.Creation) != 0; }
        }

        public bool IsRecreation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return (Status & SuspendReasonStatus.Recreation) != 0; }
        }

        public bool IsTermination
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return (Status & SuspendReasonStatus.Termination) != 0; }
        }

        public bool IsUserRequest
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return (Status & SuspendReasonStatus.UserRequest) != 0; }
        }

        public bool IsWaitingForChildren
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return (Status & SuspendReasonStatus.WaitingForChildren) != 0; }
        }
    }
}

