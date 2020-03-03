//-----------------------------------------------------------------------
// <copyright file="FileSystemMessageSinkActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Persistence;
using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// A file system <see cref="MessageSink"/> implementation
    /// </summary>
    public class FileSystemMessageSink : MessageSink
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
    public class FileSystemMessageSinkActor : TestCoordinatorEnabledMessageSink
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
            if (_reportStatus)
                Console.WriteLine("Writing test state to: {0}", Path.GetFullPath(FileName));
            try
            {
                FileStore.SaveTestRun(FileName, tree);
            }
            catch (Exception ex) //avoid throwing exception back to parent - just continue
            {
                if (_reportStatus)
                    Console.WriteLine("Failed to write test state to {0}. Cause: {1}", Path.GetFullPath(FileName), ex);                
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

