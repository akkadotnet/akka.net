//-----------------------------------------------------------------------
// <copyright file="IteratorAdapterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using Akka.Streams.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Util
{
    public class IteratorAdapterSpec
    {
        [Fact]
        public void IteratorAdapter_original_exception_should_be_preserved()
        {
            var iteratorAdapter = new IteratorAdapter<object>(new ThrowExceptioEnumerator<object>());
            Action action = () => iteratorAdapter.Next();

            action.ShouldThrow<AggregateException>()
                .And.StackTrace
                .Contains("at Akka.Streams.Tests.Util.IteratorAdapterSpec.ThrowExceptioEnumerator`1.MoveNext()");
        }

        class ThrowExceptioEnumerator<T> : IEnumerator<T>
        {
            public T Current => throw new NotImplementedException();

            object IEnumerator.Current => throw new NotImplementedException();

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public bool MoveNext()
            {
                throw new NullReferenceException("ups");
            }

            public void Reset()
            {
                throw new NotImplementedException();
            }
        }
    }
}
