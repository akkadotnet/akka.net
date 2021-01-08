//-----------------------------------------------------------------------
// <copyright file="LocalScope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// This class represents a binding of an actor deployment to a local system.
    /// </summary>
    public class LocalScope : Scope , ISurrogated
    {
        /// <summary>
        /// This class represents a surrogate of a <see cref="LocalScope"/> binding.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class LocalScopeSurrogate : ISurrogate
        {
            /// <summary>
            /// Creates a <see cref="LocalScope"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that owns this router.</param>
            /// <returns>The <see cref="LocalScope"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return Instance;
            }
        }

        private LocalScope() { }
        private static readonly LocalScope _instance = new LocalScope();

        /// <summary>
        /// The singleton instance of this scope.
        /// </summary>
        public static LocalScope Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Scope" /> from this scope using another <see cref="Akka.Actor.Scope" />
        /// to backfill options that might be missing from this scope.
        ///
        /// <note>
        /// This method ignores the given scope and returns the singleton instance of this scope.
        /// </note>
        /// </summary>
        /// <param name="other">The <see cref="Akka.Actor.Scope" /> used for fallback configuration.</param>
        /// <returns>The singleton instance of this scope</returns>
        public override Scope WithFallback(Scope other)
        {
            return Instance;
        }

        /// <summary>
        /// Creates a copy of the current instance.
        ///
        /// <note>
        /// This method returns the singleton instance of this scope.
        /// </note>
        /// </summary>
        /// <returns>The singleton instance of this scope</returns>
        public override Scope Copy()
        {
            return Instance;
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="LocalScope"/>.
        /// </summary>
        /// <param name="system">The actor system that owns this router.</param>
        /// <returns>The surrogate representation of the current <see cref="LocalScope"/>.</returns>
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new LocalScopeSurrogate();
        }
    }
}
