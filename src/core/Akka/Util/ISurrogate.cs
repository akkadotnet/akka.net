//-----------------------------------------------------------------------
// <copyright file="ISurrogate.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Util
{
    /// <summary>
    /// Interface for surrogate objects that can be converted back to their original form.
    /// Surrogates are used to provide serializable versions of objects that might not be directly serializable.
    /// </summary>
    public interface ISurrogate
    {
        /// <summary>
        /// Converts this surrogate back to its original object.
        /// </summary>
        /// <param name="system">The actor system to use during conversion.</param>
        /// <returns>The original object that this surrogate represents.</returns>
        ISurrogated FromSurrogate(ActorSystem system);
    }

    /// <summary>
    /// Used for surrogate serialization.
    /// </summary>
    public interface ISurrogated
    {
        /// <summary>
        /// Converts this object to a surrogate that can be more easily serialized.
        /// </summary>
        /// <param name="system">The actor system to use during conversion.</param>
        /// <returns>A surrogate representation of this object.</returns>
        ISurrogate ToSurrogate(ActorSystem system);
    }
}

