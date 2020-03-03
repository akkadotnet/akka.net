//-----------------------------------------------------------------------
// <copyright file="VisualizerPersistentTestRunStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;

using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    /// <summary>
    /// Stores test run as a html page.
    /// </summary>
    public class VisualizerPersistentTestRunStore : IPersistentTestRunStore
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
