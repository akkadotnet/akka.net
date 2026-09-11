//-----------------------------------------------------------------------
// <copyright file="AkkaTUnitLifecycleAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Akka.TestKit.TUnit;

/// <summary>
/// Connects <see cref="TestKit"/> instances to TUnit's per-test lifecycle.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AkkaTUnitLifecycleAttribute : Attribute, ITestStartEventReceiver, ITestEndEventReceiver
{
    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public ValueTask OnTestStart(TestContext context)
    {
        if (context.Metadata.TestDetails.ClassInstance is TestKit testKit)
            testKit.OnTestStart(context);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnTestEnd(TestContext context)
    {
        if (context.Metadata.TestDetails.ClassInstance is TestKit testKit)
            testKit.OnTestEnd();

        return ValueTask.CompletedTask;
    }
}
