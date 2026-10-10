// -----------------------------------------------------------------------
//  <copyright file="MauiDetectionSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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

    // All asserts in this file are plain xunit on purpose: FluentAssertions calls GetName() on every
    // loaded assembly when an assertion fails, which throws here and hides the real failure.

    [Fact(DisplayName = "Should_ThrowCultureNotFound_When_GettingNameOfSatelliteUnderInvariantGlobalization")]
    public void Should_ThrowCultureNotFound_When_GettingNameOfSatelliteUnderInvariantGlobalization()
    {
        // Canary for the test setup itself: invariant mode must be in effect and a "cs" satellite must
        // reproduce the hazard. If this fails, the other tests in this file prove nothing.
        var satellite = LoadSatelliteAssembly();
        Assert.Throws<CultureNotFoundException>(() => satellite.GetName());
    }

    [Fact(DisplayName = "Should_NotThrow_When_SatelliteAssemblyIsLoadedUnderInvariantGlobalization")]
    public void Should_NotThrow_When_SatelliteAssemblyIsLoadedUnderInvariantGlobalization()
    {
        var satellite = LoadSatelliteAssembly();

        // The mechanism on CoreCLR: FullName is a plain string and does not build a CultureInfo.
        // (On Native AOT FullName throws for satellites too; the catch in Util covers that case.)
        Assert.StartsWith("Akka.Hosting.InvariantGlobalization.Tests.resources", satellite.FullName);
        Assert.Contains("Culture=cs", satellite.FullName);

        Assert.False(Util.DetectMaui(AppDomain.CurrentDomain.GetAssemblies()));
        Assert.False(Util.IsMauiAssembly(satellite));
    }

    [Fact(DisplayName = "Should_DetectMaui_When_ThrowingSatelliteIsListedBeforeMauiAssembly")]
    public void Should_DetectMaui_When_ThrowingSatelliteIsListedBeforeMauiAssembly()
    {
        // FullName does not throw for satellites on CoreCLR, but it does on Native AOT. A fake assembly
        // exercises the per-assembly catch in IsMauiAssembly: without it the exception would escape to
        // DetectMaui's outer catch and stop the scan before reaching the MAUI assembly.
        var assemblies = new Assembly[]
        {
            new FakeAssembly(() => throw new CultureNotFoundException("cs")),
            new FakeAssembly(() => "Microsoft.Maui, Version=10.0.0.0, Culture=neutral, PublicKeyToken=null"),
        };

        Assert.True(Util.DetectMaui(assemblies));
    }

    private sealed class FakeAssembly(Func<string?> fullName) : Assembly
    {
        public override string? FullName => fullName();
    }

    [Fact(DisplayName = "Should_ReturnFalse_When_AssemblyEnumerationFails")]
    public void Should_ReturnFalse_When_AssemblyEnumerationFails()
    {
        // Load the satellite here so this test does not depend on test order.
        LoadSatelliteAssembly();
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
