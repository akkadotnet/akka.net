//-----------------------------------------------------------------------
// <copyright file="EnumerableActorName.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Generator of sequentially numbered actor names.
    /// </summary>
    public abstract class EnumerableActorName
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="prefix">TBD</param>
        /// <returns>TBD</returns>
        public static EnumerableActorName Create(string prefix)
        {
            return new EnumerableActorNameImpl(prefix, new AtomicCounterLong(0L));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract string Next();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="newPrefix">TBD</param>
        /// <returns>TBD</returns>
        public abstract EnumerableActorName Copy(string newPrefix);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class EnumerableActorNameImpl : EnumerableActorName
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Prefix;
        private readonly AtomicCounterLong _counter;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="prefix">TBD</param>
        /// <param name="counter">TBD</param>
        public EnumerableActorNameImpl(string prefix, AtomicCounterLong counter)
        {
            Prefix = prefix;
            _counter = counter;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string Next() => Prefix + "-" + _counter.GetAndIncrement();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="newPrefix">TBD</param>
        /// <returns>TBD</returns>
        public override EnumerableActorName Copy(string newPrefix) => new EnumerableActorNameImpl(newPrefix, _counter);
    }
}
