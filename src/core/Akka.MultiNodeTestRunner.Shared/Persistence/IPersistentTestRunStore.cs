﻿//-----------------------------------------------------------------------
// <copyright file="IPersistentTestRunStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    /// <summary>
    /// Persistent store for saving <see cref="TestRunTree"/> instances
    /// from disk.
    /// </summary>
    public interface IPersistentTestRunStore
    {
        bool SaveTestRun(string filePath, TestRunTree data);
    }
}

