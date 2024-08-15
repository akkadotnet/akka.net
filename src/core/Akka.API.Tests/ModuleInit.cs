//-----------------------------------------------------------------------
// <copyright file="ModuleInit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using VerifyTests;
using VerifyXunit;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.ScrubLinesContaining("[assembly: ReleaseDateAttribute(");
        Verifier.UseProjectRelativeDirectory("verify");
        VerifierSettings.UniqueForRuntime();
        VerifierSettings.InitializePlugins();
    }
}
