//-----------------------------------------------------------------------
// <copyright file="PartialAction.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Tools.MatchHandler
{
    /// <summary>
    /// An action that returns <c>true</c> if the <paramref name="item"/> was handled.
    /// </summary>
    /// <typeparam name="T">The type of the argument</typeparam>
    /// <param name="item">The argument.</param>
    /// <returns>Returns <c>true</c> if the <paramref name="item"/> was handled</returns>
    public delegate bool PartialAction<in T>(T item);
}

