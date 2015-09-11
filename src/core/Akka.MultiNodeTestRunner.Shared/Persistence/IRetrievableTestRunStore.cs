// -----------------------------------------------------------------------
//  <copyright file="IRetrievableTestRunStore.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    /// <summary>
    /// Persistent store for retreiving <see cref="TestRunTree" /> instances
    /// from disk.
    /// </summary>
    public interface IRetrievableTestRunStore :IPersistentTestRunStore
    {
        bool TestRunExists(string filePath);

        TestRunTree FetchTestRun(string filePath);
    }
}