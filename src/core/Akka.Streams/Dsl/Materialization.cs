using System;
using System.Reactive.Streams;

namespace Akka.Streams.Dsl
{

    /// <summary>
    /// Convenience functions for often-encountered purposes like keeping only the
    /// left (first) or only the right (second) of two input values.
    /// </summary> 
    public static class Keep
    {
        public static TLeft Left<TLeft, TRight>(TLeft left, TRight right)
        {
            return left;
        }

        public static TRight Right<TLeft, TRight>(TLeft left, TRight right)
        {
            return right;
        }

        public static Tuple<TLeft, TRight> Both<TLeft, TRight>(TLeft left, TRight right)
        {
            return Tuple.Create(left, right);
        }

        public static Unit None<TLeft, TRight>(TLeft left, TRight right)
        {
            return Unit.Instance;
        }
    }
}