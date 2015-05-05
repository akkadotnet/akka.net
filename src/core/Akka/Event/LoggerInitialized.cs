//-----------------------------------------------------------------------
// <copyright file="LoggerInitialized.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Message used to notify that a logger has been initialized.
    /// </summary>
    public class LoggerInitialized : INoSerializationVerificationNeeded
    {
    }
}

