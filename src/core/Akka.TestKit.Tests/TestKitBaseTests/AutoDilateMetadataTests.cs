//-----------------------------------------------------------------------
// <copyright file="AutoDilateMetadataTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Akka.TestKit.Internal;
using Xunit;

namespace Akka.TestKit.Tests.TestKitBaseTests;

public class AutoDilateMetadataTests
{
    [Fact]
    public void AutoDilateAttribute_should_be_parameter_only_metadata()
    {
        var usage = typeof(AutoDilateAttribute).GetCustomAttribute<AttributeUsageAttribute>();

        Assert.NotNull(usage);
        Assert.Equal(AttributeTargets.Parameter, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }

    [Fact]
    public void AutoDilateAttribute_should_only_mark_duration_parameters()
    {
        var annotatedParameters = typeof(TestKitBase).Assembly
            .GetTypes()
            .SelectMany(type => type
                .GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                            BindingFlags.NonPublic)
                .OfType<MethodBase>())
            .SelectMany(method => method.GetParameters())
            .Where(HasAutoDilateAttribute)
            .ToArray();

        Assert.NotEmpty(annotatedParameters);
        Assert.All(annotatedParameters, parameter =>
            Assert.True(
                parameter.ParameterType == typeof(TimeSpan) ||
                parameter.ParameterType == typeof(TimeSpan?),
                $"{parameter.Member.DeclaringType?.FullName}.{parameter.Member.Name} parameter " +
                $"'{parameter.Name}' has unexpected type {parameter.ParameterType}."));
    }

    [Fact]
    public void Automatically_dilated_TestKit_parameters_should_be_marked()
    {
        AssertAutoDilated(typeof(TestKitBase), nameof(TestKitBase.Dilated), "duration", "duration");
        AssertAutoDilated(typeof(TestKitBase), nameof(TestKitBase.RemainingOrDilated), "duration", "duration");
        AssertAutoDilated(typeof(TestKitBase), nameof(TestKitBase.AwaitAssert), "duration",
            "assertion", "duration", "interval", "cancellationToken");
        AssertAutoDilated(typeof(TestKitBase), nameof(TestKitBase.ReceiveWhile), "max",
            "filter", "max", "idle", "msgs", "cancellationToken");
        AssertAutoDilated(typeof(TestKitBase), nameof(TestKitBase.Within), "max",
            "min", "max", "action", "hint", "epsilonValue", "cancellationToken");
        AssertAutoDilated(typeof(TestKitBase), nameof(TestKitBase.FishUntilMessageAsync), "max",
            "max", "cancellationToken");
        AssertAutoDilated(typeof(IEventFilterApplier), nameof(IEventFilterApplier.ExpectOne), "timeout",
            "timeout", "action", "cancellationToken");
        AssertAutoDilated(typeof(InternalEventFilterApplier), nameof(InternalEventFilterApplier.ExpectOne),
            "timeout", "timeout", "action", "cancellationToken");
        AssertAutoDilated(typeof(TestBarrier), nameof(TestBarrier.Await), "timeout", "timeout");

        var constructor = typeof(TestBarrier).GetConstructors()
            .Single(ctor => HasParameterNames(ctor, "testKit", "count", "defaultTimeout"));
        Assert.True(HasAutoDilateAttribute(constructor.GetParameters().Single(p => p.Name == "defaultTimeout")));
    }

    [Fact]
    public void Raw_or_conditionally_dilated_TestKit_parameters_should_not_be_marked()
    {
        AssertNotAutoDilated(typeof(TestKitBase), nameof(TestKitBase.AwaitAssert), "interval",
            "assertion", "duration", "interval", "cancellationToken");
        AssertNotAutoDilated(typeof(TestKitBase), nameof(TestKitBase.ReceiveWhile), "idle",
            "filter", "max", "idle", "msgs", "cancellationToken");
        AssertNotAutoDilated(typeof(TestKitBase), nameof(TestKitBase.Within), "min",
            "min", "max", "action", "hint", "epsilonValue", "cancellationToken");
        AssertNotAutoDilated(typeof(TestKitBase), nameof(TestKitBase.Within), "epsilonValue",
            "min", "max", "action", "hint", "epsilonValue", "cancellationToken");
        AssertNotAutoDilated(typeof(TestKitBase), nameof(TestKitBase.ReceiveOne), "max",
            "max", "cancellationToken");
        AssertNotAutoDilated(typeof(TestKitBase), nameof(TestKitBase.AwaitConditionNoThrow), "max",
            "conditionIsFulfilled", "max", "interval", "cancellationToken");
        AssertNotAutoDilated(typeof(TestLatch), nameof(TestLatch.Ready), "timeout", "timeout");
    }

    private static void AssertAutoDilated(
        Type declaringType,
        string methodName,
        string parameterName,
        params string[] parameterNames)
    {
        var parameter = FindParameter(declaringType, methodName, parameterName, parameterNames);
        Assert.True(HasAutoDilateAttribute(parameter));
    }

    private static void AssertNotAutoDilated(
        Type declaringType,
        string methodName,
        string parameterName,
        params string[] parameterNames)
    {
        var parameter = FindParameter(declaringType, methodName, parameterName, parameterNames);
        Assert.False(HasAutoDilateAttribute(parameter));
    }

    private static ParameterInfo FindParameter(
        Type declaringType,
        string methodName,
        string parameterName,
        params string[] parameterNames)
    {
        var method = declaringType
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic)
            .Single(candidate =>
                candidate.Name == methodName &&
                HasParameterNames(candidate, parameterNames));

        return method.GetParameters().Single(parameter => parameter.Name == parameterName);
    }

    private static bool HasParameterNames(MethodBase method, params string[] parameterNames)
        => method.GetParameters().Select(parameter => parameter.Name).SequenceEqual(parameterNames);

    private static bool HasAutoDilateAttribute(ParameterInfo parameter)
        => parameter.IsDefined(typeof(AutoDilateAttribute), inherit: false);
}
