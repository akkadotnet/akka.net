//-----------------------------------------------------------------------
// <copyright file="INoSerializationVerificationNeeded.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    ///     Marker Interface INoSerializationVerificationNeeded, this interface prevents
    ///     implementing message types from being serialized if configuration setting 'akka.actor.serialize-messages' is "on"
    /// </summary>
// ReSharper disable once InconsistentNaming
    public interface INoSerializationVerificationNeeded
    {
    }

    /// <summary>
    /// Marker interface to indicate that a message might be potentially harmful;
    /// this is used to block messages coming in over remoting.
    /// </summary>
// ReSharper disable once InconsistentNaming
    public interface IPossiblyHarmful { }
}

