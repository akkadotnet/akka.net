//-----------------------------------------------------------------------
// <copyright file="AsyncRecovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IAsyncRecovery
    {
        /// <summary>
        /// Asynchronously replays persistent messages. Implementations replay
        /// a message by calling <paramref name="recoveryCallback"/>. The returned task must be completed
        /// when all messages (matching the sequence number bounds) have been replayed.
        /// The task must be completed with a failure if any of the persistent messages
        /// could not be replayed.
        /// 
        /// The <paramref name="toSequenceNr"/> is the lowest of what was returned by
        /// <see cref="ReadHighestSequenceNrAsync"/> and what the user specified as recovery
        /// <see cref="Recovery"/> parameter.
        /// This does imply that this call is always preceded by reading the highest sequence number
        /// for the given <paramref name="persistenceId"/>.
        /// 
        /// This call is NOT protected with a circuit-breaker because it may take a long time
        /// to replay all events. The plugin implementation itself must protect against an
        /// unresponsive backend store and make sure that the returned <see cref="Task"/>
        /// is completed with success or failure within reasonable time. It is not allowed to
        /// ignore completing the <see cref="Task"/>.
        /// </summary>
        /// <param name="context">The contextual information about the actor processing replayed messages.</param>
        /// <param name="persistenceId">Persistent actor identifier</param>
        /// <param name="fromSequenceNr">Inclusive sequence number where replay should start</param>
        /// <param name="toSequenceNr">Inclusive sequence number where replay should end</param>
        /// <param name="max">Maximum number of messages to be replayed</param>
        /// <param name="recoveryCallback">Called to replay a message, may be called from any thread.</param>
        /// <returns>TBD</returns>
        Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback);

        /// <summary>
        /// Asynchronously reads the highest stored sequence number for provided <paramref name="persistenceId"/>.
        /// The persistent actor will use the highest sequence number after recovery as the starting point when
        /// persisting new events.
        /// This sequence number is also used as `toSequenceNr` in subsequent calls to
        /// <see cref="ReplayMessagesAsync"/> unless the user has specified a lower `toSequenceNr`.
        /// Journal must maintain the highest sequence number and never decrease it.
        /// 
        /// This call is protected with a circuit-breaker.
        /// 
        /// Please also not that requests for the highest sequence number may be made concurrently
        /// to writes executing for the same <paramref name="persistenceId"/>, in particular it is
        /// possible that a restarting actor tries to recover before its outstanding writes have completed.
        /// </summary>
        /// <param name="persistenceId">Persistent actor identifier</param>
        /// <param name="fromSequenceNr">Hint where to start searching for the highest sequence number.
        /// When a persistent actor is recovering this <paramref name="fromSequenceNr"/> will the sequence
        /// number of the used snapshot, or `0L` if no snapshot is used.</param>
        /// <returns>TBD</returns>
        Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);
    }
}
