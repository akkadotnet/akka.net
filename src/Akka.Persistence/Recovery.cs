namespace Akka.Persistence
{
    public interface IRecovery
    {
        string PersistenceId { get; }
        long LastSequenceNr { get; }
        long SnapshotSequenceNr { get; }
    }

    public struct Recover
    {
        public Recover(SnapshotSelectionCriteria fromSnapshot, long toSequenceNr = long.MaxValue, long replayMax = long.MaxValue) 
            : this()
        {
            FromSnapshot = fromSnapshot;
            ToSequenceNr = toSequenceNr;
            ReplayMax = replayMax;
        }

        public SnapshotSelectionCriteria FromSnapshot { get; private set; }
        public long ToSequenceNr { get; private set; }
        public long ReplayMax { get; private set; }
    }
}