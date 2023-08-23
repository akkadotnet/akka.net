//-----------------------------------------------------------------------
// <copyright file="UnhandledMessageActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.TestActors
{
    /// <summary>
    /// An <see cref="UnhandledMessageActor"/> is an actor that don't handle any message
    /// sent to it. An UnhandledMessage will be generated as result of handling any message
    /// </summary>
    public class UnhandledMessageActor : ReceiveActor
    {
        
    }
}
