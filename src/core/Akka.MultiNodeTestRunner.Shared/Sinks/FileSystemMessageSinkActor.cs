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
        public FileSystemMessageSink(string assemblyName)
            : this(
                Props.Create(
                    () =>
                        new FileSystemMessageSinkActor(new JsonPersistentTestRunStore(), GenerateFileName(assemblyName),
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

        #region Static methods

        public static string GenerateFileName(string assemblyName)
        {
            return string.Format("{0}-{1}.json", assemblyName.Replace(".dll", ""), DateTime.UtcNow.Ticks);
        }

        #endregion
    }

    /// <summary>
    /// <see cref="MessageSink"/> responsible for writing to the file system.
    /// </summary>
    public class FileSystemMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        protected IPersistentTestRunStore FileStore;
        protected string FileName;

        public FileSystemMessageSinkActor(IPersistentTestRunStore store, string fileName, bool useTestCoordinator)
            : base(useTestCoordinator)
        {
            FileStore = store;
            FileName = fileName;
        }

        protected override void AdditionalReceives()
        {
            Receive<FactData>(data => ReceiveFactData(data));
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
            Console.WriteLine("Writing test state to: {0}", Path.GetFullPath(FileName));
            FileStore.SaveTestRun(FileName, tree);
        }

        protected override void ReceiveFactData(FactData data)
        {
            //Ask the TestRunCoordinator to give us the latest state
            Sender.Tell(new TestRunCoordinator.RequestTestRunState());
        }
    }
}