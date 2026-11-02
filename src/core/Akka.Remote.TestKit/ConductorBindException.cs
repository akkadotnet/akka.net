//-----------------------------------------------------------------------
// <copyright file="ConductorBindException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;

namespace Akka.Remote.TestKit;

/// <summary>
/// INTERNAL API.
///
/// Thrown when the <see cref="Controller"/> cannot bind its listener socket. The message names
/// the endpoint so a failed run says why it failed instead of showing every client node timing
/// out against a conductor that never came up.
/// </summary>
internal sealed class ConductorBindException : AkkaException
{
    private ConductorBindException(string message, Exception cause) : base(message, cause)
    {
    }

    /// <summary>
    /// Builds the exception for a failed bind, naming the port when the address was already taken.
    /// </summary>
    /// <param name="endpoint">Endpoint the controller tried to bind.</param>
    /// <param name="cause">Failure reported by the transport.</param>
    public static ConductorBindException ForEndpoint(IPEndPoint endpoint, Exception cause)
    {
        var message = IsAddressAlreadyInUse(cause)
            ? $"conductor port {endpoint.Port} already in use - could not bind TestConductor controller to {endpoint}"
            : $"could not bind TestConductor controller to {endpoint}";

        return new ConductorBindException(message, cause);
    }

    /// <summary>
    /// Walks the exception chain looking for an "address already in use" socket error. The
    /// transport wraps the socket failure, so the top level exception is not the socket one.
    /// </summary>
    private static bool IsAddressAlreadyInUse(Exception? cause)
    {
        while (cause is not null)
        {
            switch (cause)
            {
                case SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }:
                    return true;

                case AggregateException aggregate:
                    foreach (var inner in aggregate.Flatten().InnerExceptions)
                    {
                        if (IsAddressAlreadyInUse(inner))
                            return true;
                    }

                    return false;
            }

            cause = cause.InnerException;
        }

        return false;
    }
}
