//-----------------------------------------------------------------------
// <copyright file="TypeExtensionsTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util.Internal
{

    public class ExtensionsTests
    {
        [Fact]
        public void TestBetweenDoubleQuotes()
        {
            "akka.net".BetweenDoubleQuotes().ShouldBe("\"akka.net\"");
            "\"".BetweenDoubleQuotes().ShouldBe("\"\"\"");
            "".BetweenDoubleQuotes().ShouldBe("\"\"");
            ((string)null).BetweenDoubleQuotes().ShouldBe("\"\"");
        }

        [Fact]
        public void TestAlternateSelectMany()
        {
            var input = new[] { 0, 0, 1, 1, 2, 2, 1 };
            var actual = input.AlternateSelectMany(count => Enumerable.Repeat("even", count),
                count => Enumerable.Repeat("odd", count)).ToArray();
            var expectation = new[] {"even", "odd", "even", "even", "odd", "odd", "even"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesLongPathWithOneQuotedPartWithDot()
        {
            var path = @"akka.actor.deployment.""/a/dotted.path/*"".dispatcher";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] {"akka", "actor", "deployment", "/a/dotted.path/*", "dispatcher"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesSingleItem()
        {
            var path = @"akka";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] { "akka" };
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesClassicPath()
        {
            var path = @"akka.dot.net.rules";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] { "akka", "dot", "net", "rules" };
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesSingleQuotedItem()
        {
            var path = @"""akka""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] { "akka" };
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesSingleQuotedItemWithDot()
        {
            var path = @"""ak.ka""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] { "ak.ka" };
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesLongPathWithTwoQuotedParts()
        {
            var path = @"akka.actor.deployment.""/a/dotted.path/*"".""dispatcher""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] { "akka", "actor", "deployment", "/a/dotted.path/*", "dispatcher" };
            actual.ShouldAllBeEquivalentTo(expectation);
        }
    }
}
