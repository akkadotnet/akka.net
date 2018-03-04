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

            action.ShouldThrow<AggregateException>();
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
