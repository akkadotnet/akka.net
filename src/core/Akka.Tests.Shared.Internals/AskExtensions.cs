//-----------------------------------------------------------------------
// <copyright file="AskExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
// ReSharper disable once CheckNamespace


namespace Akka.TestKit
{
    public static class AskExtensions
    {
        public static TAnswer AskAndWait<TAnswer>(this ICanTell self, object message, TimeSpan timeout)
        {
            var task = self.Ask<TAnswer>(message,timeout);
            task.Wait();
            return task.Result;
        }
    }
}

