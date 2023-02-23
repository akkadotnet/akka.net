//-----------------------------------------------------------------------
// <copyright file="Option.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Akka.Util
{
    /// <summary>
    /// Allows tracking of whether a value has be initialized (even with the default value) for both
    /// reference and value types.
    /// Useful where distinguishing between null (or zero, or false) and uninitialized is significant.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public readonly struct Option<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Option<T> Create(T value)
#pragma warning disable CS0618
            => value is null ? None : new Option<T>(value);
#pragma warning restore CS0618

        /// <summary>
        /// None.
        /// </summary>
        public static readonly Option<T> None = new Option<T>();

        // TODO: Change this to private constructor in the future
        [Obsolete("Use Option<T>.Create() instead")] 
        public Option(T value)
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value), "You can not create an Option<T> with null value. Either use Option<T>.None or use Option<T>.Create(null).");
            
            Value = value;
            HasValue = true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasValue { get; }

        public bool IsEmpty => !HasValue;

        /// <summary>
        /// TBD
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Performs an implicit conversion from <typeparamref name="T"/> to <see cref="Option{T}"/>.
        /// </summary>
        /// <param name="value">The object to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator Option<T>(T value) => Create(value);

        /// <summary>
        /// Gets option value, if any, otherwise returns default value provided
        /// </summary>
        public T GetOrElse(T fallbackValue) => HasValue ? Value : fallbackValue;

        /// <summary>
        /// Applies selector to option value, if value is set
        /// </summary>
        public Option<TNew> Select<TNew>(Func<T, TNew> selector)
        {
            if (!HasValue)
                return Option<TNew>.None;

            return selector(Value);
        }

        /// <summary>
        /// Unwraps the option value and returns it without converting it into an option.
        /// </summary>
        /// <typeparam name="TNew">The output type.</typeparam>
        /// <param name="mapper">The mapping method.</param>
        public Option<TNew> FlatSelect<TNew>(Func<T, Option<TNew>> mapper)
        {
            if (!HasValue)
                return Option<TNew>.None;

            return mapper(Value);
        }

        public bool Equals(Option<T> other)
            => HasValue == other.HasValue && EqualityComparer<T>.Default.Equals(Value, other.Value);

       
        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;
            return obj is Option<T> opt && Equals(opt);
        }
        
       
        public override int GetHashCode()
        {
            unchecked
            {
                return (EqualityComparer<T>.Default.GetHashCode(Value) * 397) ^ HasValue.GetHashCode();
            }
        }

        
        public override string ToString() => HasValue ? $"Some<{Value}>" : "None";

        /// <summary>
        /// Applies specified action to this option, if there is a value
        /// </summary>
        /// <param name="action"></param>
        public void OnSuccess(Action<T> action)
        {
            if (HasValue)
                action(Value);
        }

        public static bool operator ==(Option<T> left, Option<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Option<T> left, Option<T> right)
        {
            return !(left == right);
        }
    }
}
