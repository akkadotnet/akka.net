//-----------------------------------------------------------------------
// <copyright file="AkkaAssertEqualityComparer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Akka.TestKit.Xunit2.Internals
{
    /// <summary>
    /// Default implementation of <see cref="IEqualityComparer{T}"/> used by the Akka's xUnit.net equality assertions.
    /// Copy of xUnits code
    /// https://github.com/xunit/xunit/blob/3e6ab94ca231a6d8c86e90d6e724631a0faa33b7/src/xunit.assert/Asserts/Sdk/AssertEqualityComparer.cs
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type that is being compared.</typeparam>
    public class AkkaAssertEqualityComparer<T> : IEqualityComparer<T>
    {
        private static readonly IEqualityComparer DefaultInnerComparer = new AkkaAssertEqualityComparerAdapter<object>(new AkkaAssertEqualityComparer<object>());
        private static readonly TypeInfo NullableTypeInfo = typeof(Nullable<>).GetTypeInfo();

        private readonly Func<IEqualityComparer> _innerComparerFactory;
        private readonly bool _skipTypeCheck;

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaAssertEqualityComparer{T}" /> class.
        /// </summary>
        /// <param name="skipTypeCheck">Set to <c>true</c> to skip type equality checks.</param>
        /// <param name="innerComparer">The inner comparer to be used when the compared objects are enumerable.</param>
        public AkkaAssertEqualityComparer(bool skipTypeCheck = false, IEqualityComparer innerComparer = null)
        {
            _skipTypeCheck = skipTypeCheck;

            // Use a thunk to delay evaluation of DefaultInnerComparer
            _innerComparerFactory = () => innerComparer ?? DefaultInnerComparer;
        }

        /// <inheritdoc/>
        public bool Equals(T x, T y)
        {
            var typeInfo = typeof(T).GetTypeInfo();

            // Null?
            if(!typeInfo.IsValueType || (typeInfo.IsGenericType && typeInfo.GetGenericTypeDefinition().GetTypeInfo().IsAssignableFrom(NullableTypeInfo)))
            {
                if(object.Equals(x, default(T)))
                    return object.Equals(y, default(T));

                if(object.Equals(y, default(T)))
                    return false;
            }

            switch (x)
            {
                case IEquatable<T> equatable:
                    return equatable.Equals(y);
                case IComparable<T> comparableGeneric:
                    return comparableGeneric.CompareTo(y) == 0;
                case IComparable comparable:
                    return comparable.CompareTo(y) == 0;
                case IEnumerable enumerableX
                    when y is IEnumerable enumerableY:

                    var enumeratorX = enumerableX.GetEnumerator();
                    var enumeratorY = enumerableY.GetEnumerator();
                    var equalityComparer = _innerComparerFactory();

                    while (true)
                    {
                        var hasNextX = enumeratorX.MoveNext();
                        var hasNextY = enumeratorY.MoveNext();

                        if (!hasNextX || !hasNextY)
                            return (hasNextX == hasNextY);

                        if (!equalityComparer.Equals(enumeratorX.Current, enumeratorY.Current))
                            return false;
                    }
            }

            // Same type?
            if (!_skipTypeCheck && x.GetType() != y.GetType())
                return false;

            // Last case, rely on Object.Equals
            return object.Equals(x, y);
        }

        /// <inheritdoc/>
        public int GetHashCode(T obj) => throw new NotImplementedException();
    }
}

