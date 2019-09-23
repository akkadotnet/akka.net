﻿// -----------------------------------------------------------------------
//  <copyright file="SnapshotStoreDeleteBehavior.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    public class SnapshotStoreDeleteBehavior : SnapshotStoreSaveBehavior
    {
        public SnapshotStoreDeleteBehavior(ISnapshotStoreBehaviorSetter setter) : base(setter)
        {

        }
    }
}