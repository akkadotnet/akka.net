﻿//-----------------------------------------------------------------------
// <copyright file="ITestSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    public interface ITestSnapshotStore
    {
        SnapshotStoreSaveBehavior OnSave { get; }
        SnapshotStoreLoadBehavior OnLoad { get; }
        SnapshotStoreDeleteBehavior OnDelete { get; }
        SnapshotStoreConnectionBehavior OnConnect { get; }
    }
}
