//-----------------------------------------------------------------------
// <copyright file="Greet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#region hello-world-message
namespace HelloWorld
{
    /// <summary>
    /// Immutable message type that actor will respond to
    /// </summary>
    public class Greet
    {
        public string Who { get; private set; }

        public Greet(string who)
        {
            Who = who;
        }
    }
}
#endregion
