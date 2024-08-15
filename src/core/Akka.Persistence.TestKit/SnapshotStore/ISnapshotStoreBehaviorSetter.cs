﻿// -----------------------------------------------------------------------
//  <copyright file="ISnapshotStoreBehaviorSetter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Akka.Persistence.TestKit;

public interface ISnapshotStoreBehaviorSetter
{
    Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor);
}