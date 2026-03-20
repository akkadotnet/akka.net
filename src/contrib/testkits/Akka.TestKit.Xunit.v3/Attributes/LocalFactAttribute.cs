//-----------------------------------------------------------------------
// <copyright file="LocalFactAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Akka.TestKit.Xunit.Attributes;

/// <summary>
/// <para>
/// This custom XUnit Fact attribute will skip unit tests if the environment variable
/// "XUNIT_SKIP_LOCAL_FACT" exists and is set to the string "true"
/// </para>
/// <para>
/// Note that the original <see cref="Skip"/> property takes precedence over this attribute,
/// any unit tests with <see cref="LocalFactAttribute"/> with its <see cref="Skip"/> property
/// set will always be skipped, regardless of the environment variable content.
/// </para>
/// </summary>
[XunitTestCaseDiscoverer(typeof(FactDiscoverer))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class LocalFactAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1): Attribute, IFactAttribute
{
    private const string EnvironmentVariableName = "XUNIT_SKIP_LOCAL_FACT";

    private string? _skip;

    /// <inheritdoc />
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public bool Explicit { get; set; }

    /// <inheritdoc/>
    public Type[]? SkipExceptions { get; set; }

    /// <inheritdoc/>
    public Type? SkipType { get; set; }

    /// <inheritdoc/>
    public string? SkipUnless { get; set; }

    /// <inheritdoc/>
    public string? SkipWhen { get; set; }

    /// <inheritdoc/>
    public string? SourceFilePath { get; } = sourceFilePath;
    
    /// <inheritdoc/>
    public int? SourceLineNumber { get; } = sourceLineNumber < 1 ? null : sourceLineNumber;

    /// <inheritdoc/>
    public int Timeout { get; set; }
    
    /// <inheritdoc/>
    public string? Skip
    {
        get
        {
            var skipLocal = Environment.GetEnvironmentVariable(EnvironmentVariableName)?
                .ToLowerInvariant();
            return skipLocal is "true" ? SkipLocal ?? "Local facts are being skipped" : _skip;
        }
        set => _skip = value;
    }
    
    /// <summary>
    /// The reason why this unit test is being skipped by the <see cref="LocalFactAttribute"/>.
    /// Note that the original <see cref="FactAttribute.Skip"/> property takes precedence over this message. 
    /// </summary>
    public string? SkipLocal { get; set; }
}