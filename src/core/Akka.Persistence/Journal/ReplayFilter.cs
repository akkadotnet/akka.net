//-----------------------------------------------------------------------
// <copyright file="ReplayFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public enum ReplayFilterMode
    {
        /// <summary>
        /// TBD
        /// </summary>
        Fail,
        /// <summary>
        /// TBD
        /// </summary>
        Warn,
        /// <summary>
        /// TBD
        /// </summary>
        RepairByDiscardOld,
        /// <summary>
        /// TBD
        /// </summary>
        Disabled
    }

    /// <summary>
    /// Detect corrupt event stream during replay. It uses the <see cref="IPersistentRepresentation.WriterGuid"/> and the
    /// <see cref="IPersistentRepresentation.SequenceNr"/> in the replayed events to find events emitted by overlapping writers.
    /// </summary>
    public class ReplayFilter : ActorBase
    {
        private readonly LinkedList<ReplayedMessage>  _buffer = new LinkedList<ReplayedMessage>();
        private readonly LinkedList<string> _oldWriters = new LinkedList<string>();
        private string _writerUuid = string.Empty;
        private long _sequenceNr = -1;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistentActor">TBD</param>
        /// <param name="mode">TBD</param>
        /// <param name="windowSize">TBD</param>
        /// <param name="maxOldWriters">TBD</param>
        /// <param name="debugEnabled">TBD</param>
        public ReplayFilter(IActorRef persistentActor, ReplayFilterMode mode, int windowSize, int maxOldWriters, bool debugEnabled)
        {
            PersistentActor = persistentActor;
            Mode = mode;
            WindowSize = windowSize;
            MaxOldWriters = maxOldWriters;
            DebugEnabled = debugEnabled;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistentActor">TBD</param>
        /// <param name="mode">TBD</param>
        /// <param name="windowSize">TBD</param>
        /// <param name="maxOldWriters">TBD</param>
        /// <param name="debugEnabled">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown for a number of reasons. These include:
        /// <ul>
        /// <li>The specified <paramref name="windowSize"/> is less than or equal to zero.</li>
        /// <li>The specified <paramref name="maxOldWriters"/> is less than or equal to zero.</li>
        /// <li>The specified <paramref name="mode"/> is <see cref="ReplayFilterMode.Disabled"/>.</li>
        /// </ul>
        /// </exception>
        /// <returns>TBD</returns>
        public static Props Props(IActorRef persistentActor, ReplayFilterMode mode, int windowSize, int maxOldWriters, bool debugEnabled)
        {
            if (windowSize <= 0)
                throw new ArgumentNullException(nameof(windowSize), "windowSize must be > 0");
            if (maxOldWriters <= 0)
                throw new ArgumentNullException(nameof(maxOldWriters), "maxOldWriters must be > 0");
            if (mode == ReplayFilterMode.Disabled)
                throw new ArgumentNullException(nameof(mode), "mode must not be Disabled");
            return Actor.Props.Create(() => new ReplayFilter(persistentActor, mode, windowSize, maxOldWriters, debugEnabled));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef PersistentActor { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public ReplayFilterMode Mode { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int WindowSize { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxOldWriters { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool DebugEnabled { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the <see cref="Mode"/> is set to <see cref="ReplayFilterMode.Disabled"/>.
        /// </exception>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when either the replayed event is in the wrong order or from an old writer.
        /// </exception>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if (message is ReplayedMessage)
            {
                var r = (ReplayedMessage) message;
                if (DebugEnabled && _log.IsDebugEnabled)
                    _log.Debug($"Replay: {r.Persistent}");

                try
                {
                    if (_buffer.Count == WindowSize)
                    {
                        var msg = _buffer.First;
                        _buffer.RemoveFirst();
                        PersistentActor.Tell(msg.Value, ActorRefs.NoSender);
                    }

                    if (r.Persistent.WriterGuid.Equals(_writerUuid))
                    {
                        // from same writer
                        if (r.Persistent.SequenceNr < _sequenceNr)
                        {
                            var errMsg = $@"Invalid replayed event [sequenceNr={r.Persistent.SequenceNr}, writerUUID={r.Persistent.WriterGuid}] as
                                            the sequenceNr should be equal to or greater than already-processed event [sequenceNr={_sequenceNr}, writerUUID={_writerUuid}] from the same writer, for the same persistenceId [{r.Persistent.PersistenceId}].
                                            Perhaps, events were journaled out of sequence, or duplicate PersistentId for different entities?";
                            LogIssue(errMsg);
                            switch (Mode)
                            {
                                case ReplayFilterMode.RepairByDiscardOld:
                                    //discard
                                    break;
                                case ReplayFilterMode.Fail:
                                    throw new IllegalStateException(errMsg);
                                case ReplayFilterMode.Warn:
                                    _buffer.AddLast(r);
                                    break;
                                case ReplayFilterMode.Disabled:
                                    throw new ArgumentException("Mode must not be Disabled");
                            }
                        }
                        else
                        {
                            // note that it is alright with == _sequenceNr, since such may be emitted by EventSeq
                            _buffer.AddLast(r);
                            _sequenceNr = r.Persistent.SequenceNr;
                        }
                    }
                    else if (_oldWriters.Contains(r.Persistent.WriterGuid))
                    {
                        // from old writer
                        var errMsg = $@"Invalid replayed event [sequenceNr={r.Persistent.SequenceNr}, writerUUID={r.Persistent.WriterGuid}].
                                        There was already a newer writer whose last replayed event was [sequenceNr={_sequenceNr}, writerUUID={_writerUuid}] for the same persistenceId [{r.Persistent.PersistenceId}].
                                        Perhaps, the old writer kept journaling messages after the new writer created, or duplicate PersistentId for different entities?";
                        LogIssue(errMsg);
                        switch (Mode)
                        {
                            case ReplayFilterMode.RepairByDiscardOld:
                                //discard
                                break;
                            case ReplayFilterMode.Fail:
                                throw new IllegalStateException(errMsg);
                            case ReplayFilterMode.Warn:
                                _buffer.AddLast(r);
                                break;
                            case ReplayFilterMode.Disabled:
                                throw new ArgumentException("Mode must not be Disabled");
                        }
                    }
                    else
                    {
                        // from new writer
                        if (!string.IsNullOrEmpty(_writerUuid))
                            _oldWriters.AddLast(_writerUuid);
                        if (_oldWriters.Count > MaxOldWriters)
                            _oldWriters.RemoveFirst();
                        _writerUuid = r.Persistent.WriterGuid;
                        _sequenceNr = r.Persistent.SequenceNr;

                        // clear the buffer from messages from other writers with higher SequenceNr
                        var node = _buffer.First;
                        while (node != null)
                        {
                            var next = node.Next;
                            var msg = node.Value;
                            if (msg.Persistent.SequenceNr >= _sequenceNr)
                            {
                                var errMsg = $@"Invalid replayed event [sequenceNr=${r.Persistent.SequenceNr}, writerUUID=${r.Persistent.WriterGuid}] from a new writer.
                                                An older writer already sent an event [sequenceNr=${msg.Persistent.SequenceNr}, writerUUID=${msg.Persistent.WriterGuid}] whose sequence number was equal or greater for the same persistenceId [${r.Persistent.PersistenceId}].
                                                Perhaps, the new writer journaled the event out of sequence, or duplicate PersistentId for different entities?";
                                LogIssue(errMsg);
                                switch (Mode)
                                {
                                    case ReplayFilterMode.RepairByDiscardOld:
                                        _buffer.Remove(node);
                                        //discard
                                        break;
                                    case ReplayFilterMode.Fail:
                                        throw new IllegalStateException(errMsg);
                                    case ReplayFilterMode.Warn:
                                        // keep
                                        break;
                                    case ReplayFilterMode.Disabled:
                                        throw new ArgumentException("Mode must not be Disabled");
                                }
                            }
                            node = next;
                        }
                        _buffer.AddLast(r);
                    }

                }
                catch (IllegalStateException ex)
                {
                    if (Mode == ReplayFilterMode.Fail)
                        Fail(ex);
                    else
                        throw;
                }
            }
            else if (message is RecoverySuccess || message is ReplayMessagesFailure)
            {
                if (DebugEnabled)
                    _log.Debug($"Replay completed: {message}");

                SendBuffered();
                PersistentActor.Tell(message, ActorRefs.NoSender);
                Context.Stop(Self);
            }
            else return false;
            return true;
        }

        private void SendBuffered()
        {
            foreach (var message in _buffer)
            {
                PersistentActor.Tell(message, ActorRefs.NoSender);
            }
            _buffer.Clear();
        }

        private void LogIssue(string errMsg)
        {
            switch (Mode)
            {
                case ReplayFilterMode.Warn:
                case ReplayFilterMode.RepairByDiscardOld:
                    _log.Warning(errMsg);
                    break;
                case ReplayFilterMode.Fail:
                    _log.Error(errMsg);
                    break;
                case ReplayFilterMode.Disabled:
                    throw new ArgumentException("mode must not be Disabled");
            }
        }

        private void Fail(IllegalStateException cause)
        {
            _buffer.Clear();
            PersistentActor.Tell(new ReplayMessagesFailure(cause), ActorRefs.NoSender);
            Context.Become(message =>
            {
                if (message is ReplayedMessage)
                {
                    // discard
                }
                else if (message is RecoverySuccess || message is ReplayMessagesFailure)
                {
                    Context.Stop(Self);
                }
                else
                {
                    return false;
                }

                return true;
            });
        }
    }
}
