// -----------------------------------------------------------------------
//  <copyright file="MemoryQueryJournalHelpers.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Query.InMemory;

/// <summary>
/// INTERNAL API
/// </summary>
internal static class MemoryQueryJournalHelpers
{
    public static EventEnvelope PrepareEnventEnvelope(IPersistentRepresentation message, Offset? offsetHint = null)
    {
        // Bugfix for https://github.com/akkadotnet/akka.net/issues/7528
        var payload = message.Payload is Tagged t ? t.Payload : message.Payload;
        var tags = message.Payload is Tagged tagged ? tagged.Tags.ToArray() : [];
            
        return new EventEnvelope(
            offset: offsetHint ?? new Sequence(message.SequenceNr),
            persistenceId: message.PersistenceId,
            sequenceNr: message.SequenceNr,
            @event: payload,
            timestamp: message.Timestamp,
            tags: tags);
    }
}