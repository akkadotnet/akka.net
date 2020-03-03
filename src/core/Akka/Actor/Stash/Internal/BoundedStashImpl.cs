//-----------------------------------------------------------------------
// <copyright file="BoundedStashImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL
    /// A stash implementation that is bounded
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class BoundedStashImpl : AbstractStash
    {
        /// <summary>INTERNAL
        /// A stash implementation that is bounded
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="capacity">TBD</param>
        public BoundedStashImpl(IActorContext context, int capacity = 100)
            : base(context, capacity)
        {
        }
    }
}

