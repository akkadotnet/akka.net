﻿// -----------------------------------------------------------------------
//  <copyright file="Mute.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Internal;

namespace Akka.TestKit.TestEvent;

/// <summary>
///     TBD
/// </summary>
public sealed class Mute : INoSerializationVerificationNeeded
{
    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="filters">TBD</param>
    public Mute(params EventFilterBase[] filters)
    {
        Filters = filters;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="filters">TBD</param>
    public Mute(IReadOnlyCollection<EventFilterBase> filters)
    {
        Filters = filters;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <returns>TBD</returns>
    public IReadOnlyCollection<EventFilterBase> Filters { get; }
}