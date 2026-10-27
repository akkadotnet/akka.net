// -----------------------------------------------------------------------
//  <copyright file="MauiDetectionSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;

namespace Akka.Hosting.InvariantGlobalization.Tests;

public sealed class MauiDetectionSpecs
{
    private static Assembly LoadSatelliteAssembly()
    {
        // built from Satellite.cs.resx
        var path = Path.Combine(AppContext.BaseDirectory, "cs", "Akka.Hosting.InvariantGlobalization.Tests.resources.dll");
        Assert.True(File.Exists(path), "the test project must emit a 'cs' satellite assembly");
        return Assembly.LoadFrom(path);
    }

    [Fact(DisplayName = "Should_NotThrow_When_SatelliteAssemblyIsLoadedUnderInvariantGlobalization")]
    public void Should_NotThrow_When_SatelliteAssemblyIsLoadedUnderInvariantGlobalization()
    {
        // This whole test project runs with InvariantGlobalization=true, where Assembly.GetName() throws
        // CultureNotFoundException for satellite assemblies. Plain xunit asserts on purpose:
        // FluentAssertions calls GetName() on every loaded assembly when an assertion fails.
        var satellite = LoadSatelliteAssembly();
        Assert.Contains("Culture=cs", satellite.FullName);

        Assert.False(Util.DetectMaui(AppDomain.CurrentDomain.GetAssemblies()));
        Assert.False(Util.IsMauiAssembly(satellite));
    }

    [Fact(DisplayName = "Should_ReturnFalse_When_AssemblyEnumerationFails")]
    public void Should_ReturnFalse_When_AssemblyEnumerationFails()
    {
        Assert.False(Util.DetectMaui(Throwing()));
        return;

        static IEnumerable<Assembly> Throwing()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                yield return asm;
            throw new InvalidOperationException("boom");
        }
    }

    [Fact(DisplayName = "Should_DetectMaui_When_AssemblyNameStartsWithMicrosoftMaui")]
    public void Should_DetectMaui_When_AssemblyNameStartsWithMicrosoftMaui()
    {
        Assert.False(Util.IsMauiAssembly(null));
        Assert.False(Util.IsMauiAssembly(typeof(Util).Assembly));

        Assert.True(Util.IsMauiAssemblyName("Microsoft.Maui, Version=9.0.0.0, Culture=neutral, PublicKeyToken=null"));
        Assert.True(Util.IsMauiAssemblyName("Microsoft.Maui.Controls, Version=9.0.0.0, Culture=neutral, PublicKeyToken=null"));
        Assert.False(Util.IsMauiAssemblyName("Akka.Hosting, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));
        Assert.False(Util.IsMauiAssemblyName(null));
    }
}
