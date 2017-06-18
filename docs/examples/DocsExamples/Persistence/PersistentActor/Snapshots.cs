using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class Snapshots
    {
        public class MyPersistentActor : UntypedPersistentActor
        {
            public override string PersistenceId => "my-stable-persistence-id";
            private const int SnapShotInterval = 1000;
            private object state = new object();

            protected override void OnRecover(object message)
            {
                // handle recovery here
            }

            protected override void OnCommand(object message)
            {
                if (message is SaveSnapshotSuccess s)
                {
                    // ...
                }
                else if (message is SaveSnapshotFailure f)
                {
                    // ...
                }
                else if (message is string cmd)
                {
                    Persist($"evt-{cmd}", e =>
                    {
                        UpdateState(e);
                        if (LastSequenceNr % SnapShotInterval == 0 && LastSequenceNr != 0)
                        {
                            SaveSnapshot(state);
                        }
                    });
                }
            }

            private void UpdateState(string e)
            {

            }
        }
    }
}
