//-----------------------------------------------------------------------
// <copyright file="LoggerInitialized.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a message used to notify subscribers that a logger has been initialized.
    /// </summary>
    public class LoggerInitialized : INoSerializationVerificationNeeded
    {
    }
}

