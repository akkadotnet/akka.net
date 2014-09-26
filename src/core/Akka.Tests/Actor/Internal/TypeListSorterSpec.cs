using System;
using System.Collections.Generic;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor.Internal
{
    public class TypeListSorterSpec
    {
        [Fact]
        public void Should_sort_subtypes_before_supertypes()
        {
            var types = new List<Type>
            {
                typeof(ExceptionA),
                typeof(ExceptionASub),
                typeof(ExceptionASubSub),
                typeof(Exception),
                typeof(ExceptionB),
                typeof(ExceptionBSub),
            };
            var sorted = TypeListSorter.SortSoSubtypesPrecedeTheirSupertypes(types, t => t);
            var indexExceptionA = sorted.IndexOf(typeof(ExceptionA));
            var indexExceptionASub = sorted.IndexOf(typeof(ExceptionASub));
            var indexExceptionASubSub = sorted.IndexOf(typeof(ExceptionASubSub));
            var indexExceptionB = sorted.IndexOf(typeof(ExceptionB));
            var indexExceptionBSub = sorted.IndexOf(typeof(ExceptionBSub));
            var indexException = sorted.IndexOf(typeof(Exception));

            indexExceptionASubSub.ShouldBeLessThan(indexExceptionASub);
            indexExceptionASub.ShouldBeLessThan(indexExceptionA);
            indexExceptionA.ShouldBeLessThan(indexException);
            indexExceptionBSub.ShouldBeLessThan(indexExceptionB);
            indexExceptionB.ShouldBeLessThan(indexException);
        }



        //  Exception
        //     |
        //     +-- ExceptionA
        //     |       |
        //     |      ExceptionASub
        //     |       |
        //     |      ExceptionASubSub
        //     |
        //     +-- ExceptionB
        //             |
        //            ExceptionBSub
        private class ExceptionA : Exception { }
        private class ExceptionASub : ExceptionA { }
        private class ExceptionASubSub : ExceptionASub { }
        private class ExceptionB : Exception { }
        private class ExceptionBSub : ExceptionB { }
    }
}