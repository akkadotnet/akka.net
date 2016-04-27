//-----------------------------------------------------------------------
// <copyright file="EnumerableActorName.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public static EnumerableActorName Create(string prefix)
        {
            return new EnumerableActorNameImpl(prefix, new AtomicCounterLong(0L));
        }

        public abstract string Next();

        public abstract EnumerableActorName Copy(string newPrefix);
    }

    internal class EnumerableActorNameImpl : EnumerableActorName
    {
        public readonly string Prefix;
        private readonly AtomicCounterLong _counter;

        public EnumerableActorNameImpl(string prefix, AtomicCounterLong counter)
        {
            Prefix = prefix;
            _counter = counter;
        }

        public override string Next() => Prefix + "-" + _counter.GetAndIncrement();

        public override EnumerableActorName Copy(string newPrefix) => new EnumerableActorNameImpl(newPrefix, _counter);
    }
}