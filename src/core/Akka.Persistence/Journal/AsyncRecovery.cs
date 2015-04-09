//-----------------------------------------------------------------------
// <copyright file="AsyncRecovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Persistence.Journal
{
    public interface IAsyncRecovery
    {
        /// <summary>
        /// Asynchronously replays persistent messages. Replay a message by calling <paramref name="replayCallback"/>.
        /// Returned task must be completed then all messages (matching the sequence upper bounds) have been replayed.
        /// If any of the persistent messages couldn't be replayed, task must return failure.
        /// 
        /// <paramref name="replayCallback"/> must be called with messages that have been marked as deleted.
        /// </summary>
        /// <param name="persistenceId">Persistent actor identifier</param>
        /// <param name="fromSequenceNr">Inclusive sequence number where replay should start</param>
        /// <param name="toSequenceNr">Inclusive sequence number where replay should end</param>
        /// <param name="max">Maximum number of messages to be replayed</param>
        /// <param name="replayCallback">Called to replay a message, may be called from any thread.</param>
        /// <returns></returns>
        Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback);

        /// <summary>
        /// Asynchronously reads the highest stored sequence number for provided <paramref name="persistenceId"/>.
        /// </summary>
        /// <param name="persistenceId">Persistent actor identifier</param>
        /// <param name="fromSequenceNr">Heuristic where to start searching for the highest sequence number</param>
        /// <returns></returns>
        Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);
    }
}

