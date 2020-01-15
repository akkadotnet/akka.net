//-----------------------------------------------------------------------
// <copyright file="ExtensionsTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
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
            ((string) null).BetweenDoubleQuotes().ShouldBe("\"\"");
        }

        [Fact]
        public void TestAlternateSelectMany()
        {
            var input = new[] {0, 0, 1, 1, 2, 2, 1};
            var actual = AlternateSelectMany(input, count => Enumerable.Repeat("even", count),
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
            var expectation = new[] {"akka"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesClassicPath()
        {
            var path = @"akka.dot.net.rules";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] {"akka", "dot", "net", "rules"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesSingleQuotedItem()
        {
            var path = @"""akka""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] {"akka"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesSingleQuotedItemWithDot()
        {
            var path = @"""ak.ka""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] {"ak.ka"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact]
        public void TestSplitDottedPathHonouringQuotesHandlesLongPathWithTwoQuotedParts()
        {
            var path = @"akka.actor.deployment.""/a/dotted.path/*"".""dispatcher""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] {"akka", "actor", "deployment", "/a/dotted.path/*", "dispatcher"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

        [Fact(DisplayName = "SplitDottedPathHonouringQuotes handles dots next to quotes")]
        public void SplitDottedPathHonouringQuotesHandlesDotsNextToQuotes()
        {
            var path = @""".akka."".""actor."".deployment."".""."".dispatcher""";
            var actual = path.SplitDottedPathHonouringQuotes().ToArray();
            var expectation = new[] {".akka.", "actor.", "deployment", ".", ".dispatcher"};
            actual.ShouldAllBeEquivalentTo(expectation);
        }

#if FSCHECK
        [Property]
        public void SplitDottedPathHonouringQuotesWithTestOracle()
        {
            Arb.Register<ConfigStringsGen>();
            Prop.ForAll<string>(s =>
                    SplitDottedPathHonouringQuotesOracle(s).SequenceEqual(s.SplitDottedPathHonouringQuotes()))
                .QuickCheckThrowOnFailure();
        }
#endif

        private static IEnumerable<string> SplitDottedPathHonouringQuotesOracle(string path)
        {
            return AlternateSelectMany(path.Split('\"'),
                outsideQuote => outsideQuote.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries),
                insideQuote => new[] {insideQuote});
        }

        internal class ConfigStringsGen
        {
            public static Arbitrary<string> EventLocations()
            {
                return Arb.From(ConfigStrings());
            }

            static Gen<string> ConfigStrings()
            {
                var z =
                    from size in Gen.Choose(1, 50)
                    let letters = Arb.toGen(Arb.Default.Char().Filter(c => (c >= 'A' && c <= 'z') || c == '.'))
                    let len = Gen.Choose(1, 200)
                    let words = len
                        .SelectMany(i => Gen.ArrayOf(i, letters)
                            .SelectMany(ls => Arb.Generate<bool>()
                                .Select(b => ls.Contains('.') || b ? "\"" + new string(ls) + "\"" : new string(ls))))
                    from wx in Gen.ArrayOf(size, words).Select(ww => String.Join(".", ww))
                    select wx;
                return z;
            }
        }

        /// <summary>
        /// Like selectMany, but alternates between two selectors (starting with even for item 0)
        /// </summary>
        /// <param name="self">The input sequence</param>
        /// <param name="evenSelector">The selector to use for items 0, 2, 4 etc.</param>
        /// <param name="oddSelector">The selector to use for items 1, 3, 5 etc.</param>
        /// <returns>TBD</returns>
        private static IEnumerable<TOut> AlternateSelectMany<TIn, TOut>(IEnumerable<TIn> self,
            Func<TIn, IEnumerable<TOut>> evenSelector, Func<TIn, IEnumerable<TOut>> oddSelector)
        {
            return self.SelectMany((val, i) => i % 2 == 0 ? evenSelector(val) : oddSelector(val));
        }
    }
}
