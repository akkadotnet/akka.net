//-----------------------------------------------------------------------
// <copyright file="IPersistentTestRunStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.MultiNode.TestAdapter.Internal.Reporting;

namespace Akka.MultiNode.TestAdapter.Internal.Persistence
{
    /// <summary>
    /// Persistent store for saving <see cref="TestRunTree"/> instances
    /// from disk.
    /// </summary>
    internal interface IPersistentTestRunStore
    {
        bool SaveTestRun(string filePath, TestRunTree data);
    }
}

