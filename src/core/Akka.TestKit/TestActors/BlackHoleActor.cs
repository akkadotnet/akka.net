//-----------------------------------------------------------------------
// <copyright file="BlackHoleActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit.TestActors
{
    /// <summary>
    /// A <see cref="BlackHoleActor"/> is an actor that silently 
    /// accepts any messages sent to it.
    /// </summary>
    public class BlackHoleActor : ActorBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            return true;
        }

        /// <summary>
        /// Returns a <see cref="Props"/> object that can be used to create a <see cref="BlackHoleActor"/>
        /// </summary>
        public static Props Props { get { return Props.Create<BlackHoleActor>(); } }
    }
}
