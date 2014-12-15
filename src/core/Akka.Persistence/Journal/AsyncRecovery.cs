using System;
using System.Threading.Tasks;

namespace Akka.Persistence.Journal
{
    public interface IAsyncRecovery
    {
        Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback);
        Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);
    }
}