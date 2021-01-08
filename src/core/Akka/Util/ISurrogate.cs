//-----------------------------------------------------------------------
// <copyright file="ISurrogate.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface ISurrogate
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        ISurrogated FromSurrogate(ActorSystem system);
    }

    /// <summary>
    /// Used for surrogate serialization.
    /// </summary>
    public interface ISurrogated
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        ISurrogate ToSurrogate(ActorSystem system);
    }
}

