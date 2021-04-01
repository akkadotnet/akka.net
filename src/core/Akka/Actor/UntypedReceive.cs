//-----------------------------------------------------------------------
// <copyright file="UntypedReceive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    ///     Delegate UntypedReceive
    /// </summary>
    /// <param name="message">The message.</param>
    public delegate void UntypedReceive(object message);
}

