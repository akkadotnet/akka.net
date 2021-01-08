//-----------------------------------------------------------------------
// <copyright file="MatchBuilderSignatureTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Tools.MatchHandler;
using Xunit;

namespace Akka.Tests.MatchHandler
{
    public class MatchBuilderSignatureTests
    {
        [Fact]
        public void GetHashCode_should_return_same_value_for_empty_and_null_signatures()
        {
            var nullSignature = new MatchBuilderSignature(null);
            var emptySignature = new MatchBuilderSignature(new object[0]);

            Assert.Equal(nullSignature.GetHashCode(), emptySignature.GetHashCode());
        }
        [Fact]
        public void GetHashCode_should_be_same_for_different_instances_containing_same_1_element()
        {
            var item = new object();
            var signature1 = new MatchBuilderSignature(new[] { item });
            var signature2 = new MatchBuilderSignature(new[] { item });

            Assert.Equal(signature1.GetHashCode(), signature2.GetHashCode());
        }
        [Fact]
        public void GetHashCode_should_be_same_for_different_instances_containing_same_elements()
        {
            var item1 = new object();
            var item2 = new object();
            var item3 = new object();
            var item4 = new object();
            var signature1 = new MatchBuilderSignature(new[] { item1, item2, item3, item4 });
            var signature2 = new MatchBuilderSignature(new[] { item1, item2, item3, item4 });

            Assert.Equal(signature1.GetHashCode(), signature2.GetHashCode());
        }
        [Fact]
        public void GetHashCode_should_be_different_when_elements_differs()
        {
            var item1 = new object();
            var item2 = new object();
            var item3 = new object();
            var item4 = new object();
            var signature1 = new MatchBuilderSignature(new[] { item1, item2, item3, item4 });
            var signature2 = new MatchBuilderSignature(new[] { item4, item2, item3, item4 });

            Assert.NotEqual(signature1.GetHashCode(), signature2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_should_be_different_when_order_differs()
        {
            var item1 = new object();
            var item2 = new object();
            var item3 = new object();
            var item4 = new object();
            var signature1 = new MatchBuilderSignature(new[] { item1, item2, item3, item4 });
            var signature2 = new MatchBuilderSignature(new[] { item4, item3, item2, item1 });

            Assert.NotEqual(signature1.GetHashCode(), signature2.GetHashCode());
        }

        [Fact]
        public void GetHashCode_should_be_different_when_same_elements_but_fewer()
        {
            var item1 = new object();
            var item2 = new object();
            var item3 = new object();
            var item4 = new object();
            var signature1 = new MatchBuilderSignature(new[] { item1, item2, item3, item4 });
            var signature2 = new MatchBuilderSignature(new[] { item1, item2 });

            Assert.NotEqual(signature1.GetHashCode(), signature2.GetHashCode());
        }

        [Fact]
        public void Equals_with_null_should_be_false()
        {
            var signature = new MatchBuilderSignature(new object[0]);
            Assert.False(signature.Equals((object)null));
            Assert.False(signature.Equals(null));
        }


        [Fact]
        public void Equals_with_same_should_be_true()
        {
            var signature = new MatchBuilderSignature(new object[0]);
            Assert.True(signature.Equals((object)signature));
            Assert.True(signature.Equals(signature));
        }

        [Fact]
        public void Equals_with_different_type_should_be_false()
        {
            var signature = new MatchBuilderSignature(new object[0]);
            Assert.False(signature.Equals(new object()));
        }

        [Fact]
        public void Equals_with_two_null_element_signatures_should_be_true()
        {
            var signature1 = new MatchBuilderSignature(null);
            var signature2 = new MatchBuilderSignature(null);
            Assert.True(signature1.Equals(signature2));
        }

        [Fact]
        public void Equals_with_one_null_element_signature_and_one_empty_element_signature_should_be_true()
        {
            var signature1 = new MatchBuilderSignature(null);
            var signature2 = new MatchBuilderSignature(new object[0]);
            Assert.True(signature1.Equals(signature2));
            Assert.True(signature2.Equals(signature1));
        }

        [Fact]
        public void Equals_with_one_null_element_signature_and_one_element_signature_should_be_false()
        {
            var signature1 = new MatchBuilderSignature(null);
            var signature2 = new MatchBuilderSignature(new object[] { 4711 });
            Assert.False(signature1.Equals(signature2));
            Assert.False(signature2.Equals(signature1));
        }

        [Fact]
        public void Equals_with_same_elements_in_signature_should_be_true()
        {
            var obj = new object();
            var signature1 = new MatchBuilderSignature(new object[] { 4711, "42", obj });
            var signature2 = new MatchBuilderSignature(new object[] { 4711, "42", obj });
            Assert.True(signature1.Equals(signature2));
            Assert.True(signature2.Equals(signature1));
        }
        [Fact]
        public void Equals_with_same_elements_in_signature_but_fewer_in_one_should_be_false()
        {
            var signature1 = new MatchBuilderSignature(new object[] { 4711, "42", new object() });
            var signature2 = new MatchBuilderSignature(new object[] { 4711, "42" });
            Assert.False(signature1.Equals(signature2));
            Assert.False(signature2.Equals(signature1));
        }

    }
}

