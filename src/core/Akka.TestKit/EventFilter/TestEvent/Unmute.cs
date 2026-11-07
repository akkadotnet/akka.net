//-----------------------------------------------------------------------
// <copyright file="Unmute.cs" company="Akka.NET Project">
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
/// Event-stream message that removes a collection of filters from the test event listener.
/// </summary>
public sealed class Unmute : INoSerializationVerificationNeeded
{
    private readonly IReadOnlyCollection<EventFilterBase> _filters;

    /// <summary>
    /// Creates an unmute message for the specified filters.
    /// </summary>
    /// <param name="filters">The filters to remove.</param>
    public Unmute(params EventFilterBase[] filters)
    {
        _filters = filters;
    }

    /// <summary>
    /// Creates an unmute message for the specified filters.
    /// </summary>
    /// <param name="filters">The filters to remove.</param>
    public Unmute(IReadOnlyCollection<EventFilterBase> filters)
    {
        _filters = filters;
    }

    /// <summary>
    /// Gets the filters to remove.
    /// </summary>
    /// <returns>The collection of filters included in this message.</returns>
    public IReadOnlyCollection<EventFilterBase> Filters { get { return _filters; } }
}
