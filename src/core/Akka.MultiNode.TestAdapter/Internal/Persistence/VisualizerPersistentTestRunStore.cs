//-----------------------------------------------------------------------
// <copyright file="VisualizerPersistentTestRunStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using Akka.MultiNode.TestAdapter.Internal.Reporting;

namespace Akka.MultiNode.TestAdapter.Internal.Persistence
{
    /// <summary>
    /// Stores test run as a html page.
    /// </summary>
    internal class VisualizerPersistentTestRunStore : IPersistentTestRunStore
    {
        public bool SaveTestRun(string filePath, TestRunTree data)
        {
            var template = new VisualizerRuntimeTemplate { Tree = data };
            var content = template.TransformText();
            var fullPath = Path.GetFullPath(filePath);
            File.WriteAllText(fullPath, content);

            return true;
        }
    }
}
