//-----------------------------------------------------------------------
// <copyright file="FileSystemMessageSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Persistence;
using Akka.MultiNode.TestAdapter.Internal.Reporting;

namespace Akka.MultiNode.TestAdapter.Internal.Sinks
{
    /// <summary>
    /// A file system <see cref="MessageSink"/> implementation
    /// </summary>
    internal class FileSystemMessageSink : MessageSink
    {
        public FileSystemMessageSink(string assemblyName, string platform)
            : this(
                Props.Create(
                    () =>
                        new FileSystemMessageSinkActor(new JsonPersistentTestRunStore(), 
                            FileNameGenerator.GenerateFileName(assemblyName, platform, ".json"),
                            true,
                            true)))
        {
            
        }

        public FileSystemMessageSink(Props messageSinkActorProps) : base(messageSinkActorProps)
        {
        }

        protected override void HandleUnknownMessageType(string message)
        {
            //do nothing
        }
    }

    /// <summary>
    /// <see cref="MessageSink"/> responsible for writing to the file system.
    /// </summary>
    internal class FileSystemMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        protected IPersistentTestRunStore FileStore;
        protected string FileName;
        private readonly bool _reportStatus;

        public FileSystemMessageSinkActor(IPersistentTestRunStore store, string fileName, bool reportStatus, bool useTestCoordinator)
            : base(useTestCoordinator)
        {
            FileStore = store;
            FileName = fileName;
            _reportStatus = reportStatus;
        }

        protected override void AdditionalReceives()
        {
            Receive<FactData>(data => ReceiveFactData(data));
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
            var filePath = Path.GetFullPath(FileName);
            
            // Create output dir if not exists
            var dir = new DirectoryInfo(Path.GetDirectoryName(filePath));
            if (!dir.Exists)
                dir.Create();
            
            if (_reportStatus)
                Console.WriteLine("Writing test state to: {0}", filePath);
            try
            {
                FileStore.SaveTestRun(FileName, tree);
            }
            catch (Exception ex) //avoid throwing exception back to parent - just continue
            {
                if (_reportStatus)
                    Console.WriteLine("Failed to write test state to {0}. Cause: {1}", filePath, ex);                
            }
            if (_reportStatus)
                Console.WriteLine("Finished.");           
        }

        protected override void ReceiveFactData(FactData data)
        {
            //Ask the TestRunCoordinator to give us the latest state
            Sender.Tell(new TestRunCoordinator.RequestTestRunState());
        }
    }
}

