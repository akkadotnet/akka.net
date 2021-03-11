using System;
using System.Collections.Generic;
using System.Text;
using Akka.Streams;
using Akka.Util;

namespace Akka.Remote.Artery.Utils
{
    internal static class OptionVal
    {
        public static IOptionVal<T> Apply<T>(T x)
            => x == null ? (IOptionVal<T>)Utils.None<T>.Instance : new Some<T>(x);

        public static IOptionVal<T> Some<T>(T x) => new Some<T>(x);

        public static IOptionVal<T> None<T>() => Utils.None<T>.Instance;
    }

    internal interface IOptionVal<T>
    {
        bool IsEmpty { get; }
        bool IsDefined { get; }
        T1 GetOrElse<T1>(T1 @default) where T1 : T;
        Option<T> ToOption();
        bool Contains<T1>(T1 it) where T1 : T;
        T OrNull();
        T Get { get; }
    }

    internal sealed class Some<T> : IOptionVal<T>
    {
        private readonly T _x;

        internal Some(T x)
        {
            if(x == null)
                throw new NoSuchElementException("OptionVal.Some");
            _x = x;
        }

        /// <summary>
        /// Returns true if the option is `OptionVal.None`, false otherwise.
        /// </summary>
        public bool IsEmpty => false;

        /// <summary>
        /// Returns true if the option is `OptionVal.None`, false otherwise.
        /// </summary>
        public bool IsDefined => true;

        /// <summary>
        /// Returns the option's value if the option is nonempty, otherwise return `default`.
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <param name="default"></param>
        /// <returns></returns>
        public T1 GetOrElse<T1>(T1 @default) where T1 : T
            => (T1)_x;

        /// <summary>
        ///  Convert to `Option[T]`
        /// </summary>
        public Option<T> ToOption() => new Option<T>(_x);

        public bool Contains<T1>(T1 it) where T1 : T
            => _x.Equals(it);

        /// <summary>
        /// Returns the option's value if it is nonempty, or `null` if it is empty.
        /// </summary>
        /// <returns></returns>
        public T OrNull()
            => _x;

        /// <summary>
        /// Returns the option's value.
        /// </summary>
        /// <remarks>
        /// The option must be nonEmpty.
        /// </remarks>
        /// <returns></returns>
        public T Get => _x;

        public override string ToString()
        {
            return $"Some<{GetType()}>({_x})";
        }
    }

    internal sealed class None<T> : IOptionVal<T>
    {
        public static readonly None<T> Instance = new None<T>();

        private None() {}

        public bool IsEmpty => true;
        public bool IsDefined => false;

        public T1 GetOrElse<T1>(T1 @default) where T1 : T
            => @default;

        public Option<T> ToOption()
            => Option<T>.None;

        public bool Contains<T1>(T1 it) where T1 : T
            => false;

        public T OrNull()
            => default;

        public T Get => throw new NoSuchElementException("OptionVal.None.Get");

        public override string ToString()
        {
            return $"None<{GetType()}>";
        }
    }
}
