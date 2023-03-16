// //-----------------------------------------------------------------------
// // <copyright file="Base64EncodingSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Util;
using Xunit;

namespace Akka.Tests.Util
{
    public class Base64EncodingSpec
    {
        [Fact]
        public void When_prefix_is_null_it_should_work_correctly()
        {
            var actual = Base64Encoding.Base64Encode(12345, (string)null);
            Assert.Equal("5ad", actual);
        }
    
        [Fact]
        public void Should_calculate_base_64_correctly()
        {
            var actual = Base64Encoding.Base64Encode(12345, "");
            Assert.Equal("5ad", actual);
        }
    }
}