//-----------------------------------------------------------------------
// <copyright file="UntypedReceive.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
