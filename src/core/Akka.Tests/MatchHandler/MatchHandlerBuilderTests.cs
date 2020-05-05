//-----------------------------------------------------------------------
// <copyright file="MatchHandlerBuilderTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Akka.TestKit;
using Akka.Tools.MatchHandler;
using Xunit;

namespace Akka.Tests.MatchHandler
{
    public class MatchHandlerBuilderTests : AkkaSpec
    {
        [Fact]
        public void Given_a_MatchAny_handler_has_been_added_When_adding_handler_Then_it_fails()
        {
            var builder = new MatchBuilder(new DummyCompiler<object>());
            builder.MatchAny(_ => { });

            //As we have added a handler that matches everything, adding another handler is pointless, so
            //the builder should throw an exception.
            Assert.Throws<InvalidOperationException>(() => ((Action) (() => builder.Match<object>(_ => { })))());
        }
        [Fact]
        public void Given_A_Match_object_handler_has_been_added_When_adding_handler_Then_it_fails()
        {
            var builder = new MatchBuilder(new DummyCompiler<object>());
            builder.Match<object>(_ => { });

            //As we have added a handler that matches everything, adding another handler is pointless, so
            //the builder should throw an exception.
            Assert.Throws<InvalidOperationException>(() => ((Action) (() => builder.Match<object>(_ => { })))());
        }

        [Fact]
        public void Given_A_Match_object_with_predicate_handler_has_been_added_When_adding_handler_Then_it_succeeds()
        {
            var builder = new MatchBuilder(new DummyCompiler<object>());
            builder.Match<object>(_ => { }, _ => true);

            //Although we have added a handler that matches objects, the predicate makes it uncertain if all objects matches
            //so adding a handler is ok.
            builder.Match<object>(_ => { });
        }

        [Fact]
        public void Given_an_IEnumerable_MatchBuilder_When_trying_to_match_a_type_that_is_not_an_IEnumerable_Then_it_fails()
        {
            var builder = new MatchBuilder<IEnumerable>(new DummyCompiler<IEnumerable>());

            Assert.Throws<ArgumentException>(() => ((Action) (() => builder.Match(typeof(int), _ => { })))());
        }


        [Fact]
        public void Given_an_IEnumerable_MatchBuilder_When_trying_to_match_a_type_that_is_an_IEnumerable_Then_it_succeeds()
        {
            var builder = new MatchBuilder<IEnumerable>(new DummyCompiler<IEnumerable>());

            builder.Match(typeof(List<string>), _ => { });
        }

        [Fact]
        public void Given_builder_has_built_When_adding_handler_Then_it_fails()
        {
            var builder = new MatchBuilder(new DummyCompiler<object>());
            builder.Match<object>(_ => { });
            builder.Build();

            //As we have built, no more handlers should be accepted
            Assert.Throws<InvalidOperationException>(() => ((Action) (() => builder.Match<string>(_ => { })))());
            Assert.Throws<InvalidOperationException>(() => ((Action) (() => builder.MatchAny(_ => { })))());
        }

        [Fact]
        public void Test_that_signatures_are_equal()
        {
            string str;

            AssertSameSignatures<object>(_ => { }, _ => { });

            AssertSameSignatures<object>(bldr => bldr.MatchAny(_ => { }), bldr => bldr.MatchAny(o => { str = o.ToString(); }));

            AssertSameSignatures<object>(bldr => bldr.MatchAny(_ => { }), bldr => bldr.Match<object>(o => { str = o.ToString(); }));

            AssertSameSignatures<object>(bldr => bldr.Match<string>(_ => { }), bldr => bldr.Match<string>(s => { str = s; }));

            AssertSameSignatures<object>(
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                },
                bldr =>
                {
                    bldr.Match<string>(s => { str = s; });
                    bldr.Match<bool>(b => { var x = b && true; });
                    bldr.Match<int>(i => { });
                });


            AssertSameSignatures<object>(bldr => bldr.Match<string>(s => { }, s => true), bldr => bldr.Match<string>(s => { }, s => false));

            AssertSameSignatures<object>(bldr => bldr.Match<string>(s => true), bldr => bldr.Match<string>(s => false));

            AssertSameSignatures<object>(
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { }, b => b);
                    bldr.Match<int>(i => { }, _ => true);
                    bldr.Match(typeof(ValueTuple), t => { });
                    bldr.Match(typeof(float), o => true);
                },
                bldr =>
                {
                    bldr.Match<string>(s => { str = s; });
                    bldr.Match<bool>(b => { var x = b && true; }, b => !b);
                    bldr.Match<int>(i => { }, i => i > 0);
                    bldr.Match(typeof(ValueTuple), t => { });
                    bldr.Match(typeof(float), o => false);
                });
        }

        [Fact]
        public void Test_that_signatures_differs()
        {
            AssertDifferentSignatures<object>(_ => { }, bldr => bldr.MatchAny(_ => { }));

            AssertDifferentSignatures<object>(_ => { }, bldr => bldr.Match<string>(_ => { }));

            AssertDifferentSignatures<object>(_ => { }, bldr => bldr.Match<string>(_ => true));

            AssertDifferentSignatures<object>(bldr => bldr.Match<string>(_ => { }), bldr => bldr.Match<int>(_ => { }));

            AssertDifferentSignatures<object>(bldr => bldr.Match<string>(_ => { }), bldr => bldr.Match<string>(_ => { }, s => s == null));

            AssertDifferentSignatures<object>(bldr => bldr.MatchAny(_ => { }), bldr => bldr.Match<object>(_ => { }, _ => true));

            AssertDifferentSignatures<object>(
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                },
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                    bldr.Match<int>(i => { });
                });

            AssertDifferentSignatures<object>(
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                },
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                    bldr.MatchAny(_ => { });
                });


            AssertDifferentSignatures<object>(
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                    bldr.Match(typeof(ValueTuple), t => { });
                },
                bldr =>
                {
                    bldr.Match<string>(s => { });
                    bldr.Match<bool>(b => { });
                    bldr.Match<int>(i => { });
                    bldr.Match(typeof(double), t => { });
                });

            AssertDifferentSignatures<object>(
                bldr =>
                {
                    bldr.Match(typeof(string),s => true);
                },
                bldr =>
                {
                    bldr.Match(typeof(int), s => true);
                });

        }

        private void AssertDifferentSignatures<T>(Action<MatchBuilder<T>> build1, Action<MatchBuilder<T>> build2)
        {
            TestSignature(build1, build2, false);
        }

        private void AssertSameSignatures<T>(Action<MatchBuilder<T>> build1, Action<MatchBuilder<T>> build2)
        {
            TestSignature(build1, build2, true);
        }

        private void TestSignature<T>(Action<MatchBuilder<T>> build1, Action<MatchBuilder<T>> build2, bool signaturesShouldMatch)
        {
            var compiler1 = new DummyCompiler<T>();
            var compiler2 = new DummyCompiler<T>();
            var builder1 = new MatchBuilder<T>(compiler1);
            var builder2 = new MatchBuilder<T>(compiler2);
            build1(builder1);
            build2(builder2);
            builder1.Build();
            builder2.Build();
            var signature1 = compiler1.Signature;
            var signature2 = compiler2.Signature;
            if(signaturesShouldMatch)
                Assert.Equal(signature1, signature2);
            else
                Assert.NotEqual(signature1, signature2);
        }

        private class DummyCompiler<T> : IMatchCompiler<T>
        {
            public IReadOnlyCollection<TypeHandler> Handlers;
            public IReadOnlyCollection<Argument> CapturedArguments;
            public MatchBuilderSignature Signature;

            public PartialAction<T> Compile(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature)
            {
                Handlers = handlers;
                CapturedArguments = capturedArguments;
                Signature = signature;
                return null;
            }

            public void CompileToMethod(IReadOnlyList<TypeHandler> handlers, IReadOnlyList<Argument> capturedArguments, MatchBuilderSignature signature, TypeBuilder typeBuilder, string methodName, MethodAttributes methodAttributes = MethodAttributes.Public | MethodAttributes.Static)
            {
                throw new NotImplementedException();
            }
        }
    }
}

