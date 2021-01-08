//-----------------------------------------------------------------------
// <copyright file="Scope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// This class provides base functionality when defining a system binding (e.g. local/remote/cluster) used during actor deployment.
    /// </summary>
    public abstract class Scope : IEquatable<Scope>
    {
        /// <summary>
        /// A binding that binds actor deployments to the local system.
        /// </summary>
        public static readonly LocalScope Local = LocalScope.Instance;

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Scope" /> from this scope using another <see cref="Akka.Actor.Scope" />
        /// to backfill options that might be missing from this scope.
        /// 
        /// <note>
        /// This method is immutable and returns a new instance of <see cref="Akka.Actor.Scope" />.
        /// </note>
        /// </summary>
        /// <param name="other">The <see cref="Akka.Actor.Scope" /> used for fallback configuration.</param>
        /// <returns>A new <see cref="Akka.Actor.Scope" /> using <paramref name="other" /> for fallback configuration.</returns>
        public abstract Scope WithFallback(Scope other);

        /// <summary>
        /// Creates a copy of the current instance.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.Scope"/></returns>
        public abstract Scope Copy();

        /// <inheritdoc/>
        public virtual bool Equals(Scope other)
        {
            if (other == null) return false;

            //we don't do equality checks on fallbacks
            return GetType() == other.GetType();
        }
    }

    /// <summary>
    /// This class represents a binding of an actor deployment to an unspecified system.
    /// </summary>
    internal class NoScopeGiven : Scope
    {
        private NoScopeGiven() { }

        private static readonly NoScopeGiven _instance = new NoScopeGiven();

        /// <summary>
        /// The singleton instance of this scope.
        /// </summary>
        public static NoScopeGiven Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Scope" /> from this scope using another <see cref="Akka.Actor.Scope" />
        /// to backfill options that might be missing from this scope.
        /// 
        /// <note>
        /// This method returns the given scope unaltered.
        /// </note>
        /// </summary>
        /// <param name="other">The <see cref="Akka.Actor.Scope" /> used for fallback configuration.</param>
        /// <returns>The scope passed in as the parameter.</returns>
        public override Scope WithFallback(Scope other)
        {
            return other;
        }

        /// <summary>
        /// Creates a copy of the current instance.
        ///
        /// <note>
        /// This method returns a singleton instance of this scope.
        /// </note>
        /// </summary>
        /// <returns>The singleton instance of this scope</returns>
        public override Scope Copy()
        {
            return Instance;
        }
    }
}
