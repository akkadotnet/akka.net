﻿//-----------------------------------------------------------------------
// <copyright file="AkkaAssertEqualityComparerAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace Akka.TestKit.Xunit.Internals
{
    /// <summary>
    /// A class that wraps <see cref="IEqualityComparer{T}"/> to create <see cref="IEqualityComparer"/>.
    /// Copy of xUnits class:
    /// https://github.com/xunit/xunit/blob/3e6ab94ca231a6d8c86e90d6e724631a0faa33b7/src/xunit.assert/Asserts/Sdk/AssertEqualityComparerAdapter.cs
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    /// <typeparam name="T">The type that is being compared.</typeparam>
    internal class AkkaAssertEqualityComparerAdapter<T> : IEqualityComparer
    {
        readonly IEqualityComparer<T> innerComparer;

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaAssertEqualityComparerAdapter{T}"/> class.
        /// </summary>
        /// <param name="innerComparer">The comparer that is being adapted.</param>
        public AkkaAssertEqualityComparerAdapter(IEqualityComparer<T> innerComparer)
        {
            this.innerComparer = innerComparer;
        }

        /// <inheritdoc/>
        public new bool Equals(object x, object y)
        {
            return innerComparer.Equals((T)x, (T)y);
        }

        /// <inheritdoc/>
        public int GetHashCode(object obj)
        {
            throw new NotImplementedException();
        }
    }
}

