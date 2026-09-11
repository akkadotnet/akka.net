// -----------------------------------------------------------------------
//  <copyright file="TypeCacheSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using FluentAssertions;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Tests.Configuration;

public class TypeLoaderSpecs
{
    [Fact(DisplayName = "TypeLoader must return null when type is not registered")]
    public void NonExistentTypeTest()
    {
        TypeLoader.GetType("Not.Exist").Should().BeNull();
        TypeLoader.GetType("NotExist").Should().BeNull();
    }
    
    [Fact(DisplayName = "TypeLoader must throw an exception when type is not registered and throwOnError is true")]
    public void NonExistentTypeThrowTest()
    {
        var type = typeof(Config);
        TypeLoader.Register(type);
        
        Invoking(() => TypeLoader.GetType("Not.Exist", true))
            .Should().Throw<TypeLoadException>();
        Invoking(() => TypeLoader.GetType("Not.Exist, Akka", true))
            .Should().Throw<TypeLoadException>();
        Invoking(() => TypeLoader.GetType("NotExist", true))
            .Should().Throw<TypeLoadException>();
        Invoking(() => TypeLoader.GetType(", NotExist", true))
            .Should().Throw<TypeLoadException>();
        Invoking(() => TypeLoader.GetType(", Akka", true))
            .Should().Throw<TypeLoadException>();
    }
    
    [Fact(DisplayName = "TypeLoader must return type when type is registered")]
    public void TypeTest()
    {
        var type = typeof(Config);
        TypeLoader.Register<Config>();
        TypeLoader.Register("abcd", type);
        
        TypeLoader.GetType(type.AssemblyQualifiedName!).Should().Be(type);
        TypeLoader.GetType(type.Name).Should().Be(type);
        TypeLoader.GetType("Config").Should().Be(type);
        TypeLoader.GetType("Akka.Configuration.Config").Should().Be(type);
        TypeLoader.GetType("Akka.Configuration.Config, Akka").Should().Be(type);
        TypeLoader.GetType("Config, Akka").Should().Be(type);
        TypeLoader.GetType("abcd").Should().Be(type);
    }
    
}