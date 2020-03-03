//-----------------------------------------------------------------------
// <copyright file="TypeExtensionsTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    
    public class TypeExtensionsTests
    {
        [Fact]
        public void Test_generic_implements()
        {
            typeof(object[]).Implements<IEnumerable>().ShouldBe(true);
            typeof(object[]).Implements<string>().ShouldBe(false);
            typeof(List<string>).Implements<IEnumerable<string>>().ShouldBe(true);
            typeof(List<string>).Implements<IEnumerable<int>>().ShouldBe(false);
        }

        [Fact]
        public void Test_nongeneric_implements()
        {
            typeof(object[]).Implements(typeof(IEnumerable)).ShouldBe(true);
            typeof(object[]).Implements(typeof(string)).ShouldBe(false);
            typeof(List<string>).Implements(typeof(IEnumerable<string>)).ShouldBe(true);
            typeof(List<string>).Implements(typeof(IEnumerable<int>)).ShouldBe(false);
        }

        public static IEnumerable<object[]> TargetTypes
        {
            get
            {
                yield return new object[] { typeof(ParentClass<OtherClassA, OtherClassB, OtherClassC>.ChildClass) };
                yield return new object[] { typeof(string) };
                yield return new object[] { typeof(object) };
                yield return new object[] { typeof(TypeExtensionsTests) };
            }
        }

        [Theory]
        [MemberData(nameof(TargetTypes))]
        public void Type_qualified_name_includes_only_typename_and_assemblyname(Type targetType)
        {
            var manifest = targetType.TypeQualifiedName();

            manifest.Should().NotContain("Version");
            manifest.Should().NotContain("Culture");
            manifest.Should().NotContain("PublicKeyToken");
        }

        public sealed class OtherClassA { }

        public sealed class OtherClassB { }

        public sealed class OtherClassC { }

        public sealed class ParentClass<T1, T2, T3>
        {
            public sealed class ChildClass
            {
                public string Value { get; set; }
            }
        }
    }
}

