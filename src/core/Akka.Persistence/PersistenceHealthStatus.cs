// -----------------------------------------------------------------------
//  <copyright file="PersistentHealthStatus.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Persistence;

/// <summary>
/// Used by SnapshotStore and Journal to indicate the health status of the underlying storage.
/// </summary>
public enum PersistenceHealthStatus
{
    /// <summary>
    /// Akka.Persistence is working as expected.
    /// </summary>
    Healthy = 0,
    
    /// <summary>
    /// Akka.Persistence is experiencing some issues that should be recoverable.
    /// </summary>
    Degraded = 1,
    
    /// <summary>
    /// Akka.Persistence has experienced a fatal error. 
    /// </summary>
    Unhealthy = 2,
}

/// <summary>
/// Results from a health check.
/// </summary>
public readonly record struct PersistenceHealthCheckResult(PersistenceHealthStatus Status, 
    string? Description = null, 
    Exception? Exception = null, 
    IReadOnlyDictionary<string, object>? Data = null);