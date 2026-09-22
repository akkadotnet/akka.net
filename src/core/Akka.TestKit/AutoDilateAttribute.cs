//-----------------------------------------------------------------------
// <copyright file="AutoDilateAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;

namespace Akka.TestKit;

/// <summary>
/// Marks a duration parameter whose value is automatically scaled by
/// <see cref="TestKitSettings.TestTimeFactor"/>.
/// </summary>
/// <remarks>
/// Callers should pass an undilated duration to parameters marked with this attribute.
/// Passing a value that has already been scaled by <see cref="TestKitBase.Dilated(TimeSpan)"/>
/// applies the configured time factor twice.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class AutoDilateAttribute : Attribute
{
}
