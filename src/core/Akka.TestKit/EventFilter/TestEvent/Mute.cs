//-----------------------------------------------------------------------
// <copyright file="Mute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.Internal;

#nullable enable
namespace Akka.TestKit.TestEvent;

/// <summary>
/// Event-stream message that installs a collection of filters in the test event listener.
/// </summary>
public sealed class Mute : INoSerializationVerificationNeeded
{
    private readonly IReadOnlyCollection<EventFilterBase> _filters;

    /// <summary>
    /// Creates a mute message for the specified filters.
    /// </summary>
    /// <param name="filters">The filters to install.</param>
    public Mute(params EventFilterBase[] filters)
    {
        _filters = filters;
    }

    /// <summary>
    /// Creates a mute message for the specified filters.
    /// </summary>
    /// <param name="filters">The filters to install.</param>
    public Mute(IReadOnlyCollection<EventFilterBase> filters)
    {
        _filters = filters;
    }

    /// <summary>
    /// Gets the filters to install.
    /// </summary>
    /// <returns>The collection of filters included in this message.</returns>
    public IReadOnlyCollection<EventFilterBase> Filters { get { return _filters; } }
}
