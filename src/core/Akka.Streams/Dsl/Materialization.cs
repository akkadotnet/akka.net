using System;

namespace Akka.Streams.Dsl
{

    /**
     * Convenience functions for often-encountered purposes like keeping only the
     * left (first) or only the right (second) of two input values.
     */
    public static class Keep
    {
        public static TResult Left<TLeft, TRight, TResult>(TLeft left, TRight right) where TLeft : TResult
        {
            return left;
        }

        public static TResult Right<TLeft, TRight, TResult>(TLeft left, TRight right) where TRight : TResult
        {
            return right;
        }

        public static Tuple<TLeft, TRight> Both<TLeft, TRight>(TLeft left, TRight right)
        {
            return Tuple.Create(left, right);
        }

        public static void None<TLeft, TRight>(TLeft left, TRight right) { }
    }
}