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

        protected EnumerableActorName()
        {
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

        public override string Next()
        {
            return Prefix + "-" + _counter.GetAndIncrement();
        }

        public override EnumerableActorName Copy(string newPrefix)
        {
            return new EnumerableActorNameImpl(newPrefix, _counter);
        }
    }
}