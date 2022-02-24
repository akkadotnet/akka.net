//-----------------------------------------------------------------------
// <copyright file="GreetingActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#region akka-hello-world-greeting

using System;
using Akka.Actor;

namespace HelloWorld
{
    /// <summary>
    /// The actor class
    /// </summary>
    public class GreetingActor : ReceiveActor
    {
        public GreetingActor()
        {
            // Tell the actor to respond to the Greet message
            Receive<Greet>(greet => Console.WriteLine($"Hello {greet.Who}", ConsoleColor.Green));
        }
        protected override void PreStart() => Console.WriteLine("Good Morning, we are awake!", ConsoleColor.Green);

        protected override void PostStop() => Console.WriteLine("Good Night, going to bed!", ConsoleColor.Red);
    }
}
#endregion

