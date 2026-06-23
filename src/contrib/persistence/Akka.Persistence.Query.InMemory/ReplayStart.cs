//-----------------------------------------------------------------------
// <copyright file="ReplayStart.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.Query.InMemory
{
    /// <summary>
    /// The resolved starting position for an InMemory forward replay. It is either a concrete forward
    /// <see cref="Offset"/> or a deferred "last N" position that the publisher resolves against the current matching
    /// event count at materialization. Collapsing both cases into a single value keeps "from the end" an internal
    /// detail of how the read journal interprets an <see cref="Query.Offset"/>, instead of an extra argument threaded
    /// through every publisher constructor.
    /// </summary>
    internal readonly struct ReplayStart
    {
        private ReplayStart(int offset, int fromEndCount)
        {
            Offset = offset;
            FromEndCount = fromEndCount;
        }

        /// <summary>
        /// The concrete forward start offset. Only meaningful when <see cref="IsFromEnd"/> is <c>false</c>.
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// When greater than zero, the replay begins at <c>max(0, matchingCount - FromEndCount)</c>, where
        /// <c>matchingCount</c> is read from the journal at materialization.
        /// </summary>
        public int FromEndCount { get; }

        /// <summary>
        /// True when the start must be resolved from the end of history before replaying.
        /// </summary>
        public bool IsFromEnd => FromEndCount > 0;

        /// <summary>
        /// A concrete forward start at <paramref name="offset"/>.
        /// </summary>
        public static ReplayStart At(int offset) => new(offset, 0);

        /// <summary>
        /// A deferred start at the <paramref name="count"/>-th event from the end of history.
        /// </summary>
        public static ReplayStart LastN(int count) => new(0, count);
    }
}
