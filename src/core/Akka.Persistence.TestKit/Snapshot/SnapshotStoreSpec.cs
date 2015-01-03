using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using Akka.TestKit;

namespace Akka.Persistence.TestKit.Snapshot
{
    /// <summary>
    /// This spec aims to verify custom <see cref="SnapshotStore"/> implementations. 
    /// Every custom authors snapshot store spec should have it's spec suite included.
    /// </summary>
    public abstract class SnapshotStoreSpec : PluginSpec
    {
        protected static readonly Config Config =
            ConfigurationFactory.ParseString("akka.persistence.publish-plugin-commands = on");

        private TestProbe _senderProbe;
        private IEnumerable<SnapshotMetadata> _metadata = Enumerable.Empty<SnapshotMetadata>();

        protected SnapshotStoreSpec(TestKitAssertions assertions, Config config, string actorSystemName = null, string testActorName = null)
            : base(assertions, config.WithFallback(Config), actorSystemName, testActorName)
        {
            _senderProbe = CreateTestProbe();
            _metadata = WriteSnapshots();
        }

        public ActorRef SnapshotStore { get { return Extension.SnapshotStoreFor(null); } }

        private IEnumerable<SnapshotMetadata> WriteSnapshots()
        {
            for (int i = 0; i < 5; i++)
            {
                var metadata = new SnapshotMetadata(Pid, i + 10);
                SnapshotStore.Tell(new SaveSnapshot(metadata, "s-" + i), _senderProbe.Ref);
                yield return _senderProbe.ExpectMsg<SaveSnapshotSuccess>().Metadata;
            }
        }

    }
}