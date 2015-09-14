namespace Akka.Streams.Dsl
{
    /**
     * Strategy that defines how a stream of streams should be flattened into a stream of simple elements.
     */
    public abstract class FlattenStrategy<T1, T2> { }
    public sealed class Concat<T, TMat> : FlattenStrategy<Source<T, TMat>, T> { }

    public static class FlattenStrategy
    {
        /**
         * Strategy that flattens a stream of streams by concatenating them. This means taking an incoming stream
         * emitting its elements directly to the output until it completes and then taking the next stream. This has the
         * consequence that if one of the input stream is infinite, no other streams after that will be consumed from.
         */
        public static FlattenStrategy<Source<T, TMat>, T> Concat<T, TMat>()
        {
            return new Concat<T, TMat>();
        }
    }
}