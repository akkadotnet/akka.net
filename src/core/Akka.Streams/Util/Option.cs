//-----------------------------------------------------------------------
// <copyright file="Option.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Streams.Util
{
    /// <summary>
    /// Allows tracking of whether a value has be initialized (even with the default value) for both
    /// reference and value types.
    /// Useful where distinguishing between null (or zero, or false) and unitialized is significant.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public struct Option<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Option<T> None = new Option<T>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        public Option(T value)
        {
            Value = value;
            HasValue = true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasValue { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Performs an implicit conversion from <see cref="T"/> to <see cref="Option{T}"/>.
        /// </summary>
        /// <param name="value">The object to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator Option<T>(T value) => new Option<T>(value);

        /// <summary>
        /// Determines whether the specified option, is equal to this instance.
        /// </summary>
        /// <param name="other">The option to compare with this instance.</param>
        /// <returns><c>true</c> if the specified option is equal to this instance; otherwise, <c>false</c>.</returns>
        public bool Equals(Option<T> other)
            => HasValue == other.HasValue && EqualityComparer<T>.Default.Equals(Value, other.Value);

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is Option<T> && Equals((Option<T>)obj);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (EqualityComparer<T>.Default.GetHashCode(Value) * 397) ^ HasValue.GetHashCode();
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance.
        /// </returns>
        public override string ToString() => HasValue ? $"Some<{Value}>" : "None";
    }
}