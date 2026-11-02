//-----------------------------------------------------------------------
// <copyright file="UnboundedStashImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL
    /// A stash implementation that is unbounded
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class UnboundedStashImpl : AbstractStash
    {
        /// <summary>INTERNAL
        /// A stash implementation that is bounded
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        /// <param name="context">The actor context. Must use a deque-based mailbox; otherwise an <see cref="ActorInitializationException"/> is thrown.</param>
        public UnboundedStashImpl(IActorContext context)
            : base(context)
        {
        }
        
        public override int Capacity => Deploy.NoStashSize; // stash must be unbounded
    }
}

