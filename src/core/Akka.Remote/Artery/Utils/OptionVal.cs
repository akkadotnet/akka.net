using System;
using System.Collections.Generic;
using System.Text;
using Akka.Streams;
using Akka.Util;

namespace Akka.Remote.Artery.Utils
{
    public sealed class OptionVal<T>
    {
        public static readonly OptionVal<T> None = new OptionVal<T>(default);

        private readonly T _x;

        public OptionVal(T x)
        {
            _x = x;
        }

        /// <summary>
        /// Returns true if the option is `OptionVal.None`, false otherwise.
        /// </summary>
        public bool IsEmpty => _x == null;

        /// <summary>
        /// Returns true if the option is `OptionVal.None`, false otherwise.
        /// </summary>
        public bool IsDefined => !IsEmpty;

        /// <summary>
        /// Returns the option's value if the option is nonempty, otherwise return `default`.
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <param name="default"></param>
        /// <returns></returns>
        public T1 GetOrElse<T1>(T1 @default) where T1: T
            => _x == null ? @default : (T1)_x;

        /// <summary>
        ///  Convert to `Option[T]`
        /// </summary>
        public Option<T> ToOption => new Option<T>(_x);

        public bool Contains<T1>(T1 it) where T1 : T
            => _x != null && _x.Equals(it);

        /// <summary>
        /// Returns the option's value if it is nonempty, or `null` if it is empty.
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <returns></returns>
        public T1 OrNull<T1>() where T1 : T
            => GetOrElse<T1>(default);

        /// <summary>
        /// Returns the option's value.
        /// </summary>
        /// <remarks>
        /// The option must be nonEmpty.
        /// </remarks>
        /// <returns></returns>
        public T Get()
        {
            if(_x == null)
                throw new NoSuchElementException("OptionVal.None.Get");
            return _x;
        }

        public override string ToString()
        {
            return _x == null ? "None" : $"Some({_x})";
        }
    }
}
