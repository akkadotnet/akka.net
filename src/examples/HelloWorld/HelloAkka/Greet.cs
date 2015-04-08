//-----------------------------------------------------------------------
// <copyright file="Greet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace HelloAkka
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
