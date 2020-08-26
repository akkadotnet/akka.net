//-----------------------------------------------------------------------
// <copyright file="AsyncWriteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Configuration;

namespace Akka.Persistence.Journal
{
    /// <summary>
    /// Abstract journal, optimized for asynchronous, non-blocking writes.
    /// </summary>
    public abstract class AsyncWriteJournal : WriteJournalBase, IAsyncRecovery
    {
        private static readonly TaskContinuationOptions _continuationOptions = TaskContinuationOptions.ExecuteSynchronously;
        protected readonly bool CanPublish;
        private readonly CircuitBreaker _breaker;
        private readonly ReplayFilterMode _replayFilterMode;
        private readonly bool _isReplayFilterEnabled;
        private readonly int _replayFilterWindowSize;
        private readonly int _replayFilterMaxOldWriters;
        private readonly bool _replayDebugEnabled;
        private readonly IActorRef _resequencer;

        private long _resequencerCounter = 1L;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncWriteJournal"/> class.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the Persistence extension related to this journal has not been used in the current <see cref="ActorSystem"/> context.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when an invalid <c>replay-filter.mode</c> is read from the configuration.
        /// Acceptable <c>replay-filter.mode</c> values include: off | repair-by-discard-old | fail | warn
        /// </exception>
        protected AsyncWriteJournal()
        {
            var extension = Persistence.Instance.Apply(Context.System);
            if (extension == null)
            {
                throw new ArgumentException("Couldn't initialize SyncWriteJournal instance, because associated Persistence extension has not been used in current actor system context.");
            }

            CanPublish = extension.Settings.Internal.PublishPluginCommands;
            var config = extension.ConfigFor(Self);
            _breaker = new CircuitBreaker(
                Context.System.Scheduler,
                config.GetInt("circuit-breaker.max-failures", 0),
                config.GetTimeSpan("circuit-breaker.call-timeout", null),
                config.GetTimeSpan("circuit-breaker.reset-timeout", null));

            var replayFilterMode = config.GetString("replay-filter.mode", "").ToLowerInvariant();
            switch (replayFilterMode)
            {
                case "off":
                    _replayFilterMode = ReplayFilterMode.Disabled;
                    break;
                case "repair-by-discard-old":
                    _replayFilterMode = ReplayFilterMode.RepairByDiscardOld;
                    break;
                case "fail":
                    _replayFilterMode = ReplayFilterMode.Fail;
                    break;
                case "warn":
                    _replayFilterMode = ReplayFilterMode.Warn;
                    break;
                default:
                    throw new ConfigurationException($"Invalid replay-filter.mode [{replayFilterMode}], supported values [off, repair-by-discard-old, fail, warn]");
            }
            _isReplayFilterEnabled = _replayFilterMode != ReplayFilterMode.Disabled;
            _replayFilterWindowSize = config.GetInt("replay-filter.window-size", 0);
            _replayFilterMaxOldWriters = config.GetInt("replay-filter.max-old-writers", 0);
            _replayDebugEnabled = config.GetBoolean("replay-filter.debug", false);

            _resequencer = Context.System.ActorOf(Props.Create(() => new Resequencer()));
        }

        /// <inheritdoc/>
        public abstract Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback);

        /// <inheritdoc/>
        public abstract Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr);

        /// <summary>
        /// Plugin API: asynchronously writes a batch of persistent messages to the
        /// journal.
        /// 
        /// The batch is only for performance reasons, i.e. all messages don't have to be written
        /// atomically. Higher throughput can typically be achieved by using batch inserts of many
        /// records compared to inserting records one-by-one, but this aspect depends on the
        /// underlying data store and a journal implementation can implement it as efficient as
        /// possible. Journals should aim to persist events in-order for a given `persistenceId`
        /// as otherwise in case of a failure, the persistent state may be end up being inconsistent.
        /// 
        /// Each <see cref="AtomicWrite"/> message contains the single <see cref="Persistent"/>
        /// that corresponds to the event that was passed to the 
        /// <see cref="Eventsourced.Persist{TEvent}(TEvent,Action{TEvent})"/> method of the
        /// <see cref="PersistentActor" />, or it contains several <see cref="Persistent"/>
        /// that correspond to the events that were passed to the
        /// <see cref="Eventsourced.PersistAll{TEvent}(IEnumerable{TEvent},Action{TEvent})"/>
        /// method of the <see cref="PersistentActor"/>. All <see cref="Persistent"/> of the
        /// <see cref="AtomicWrite"/> must be written to the data store atomically, i.e. all or none must
        /// be stored. If the journal (data store) cannot support atomic writes of multiple
        /// events it should reject such writes with a <see cref="NotSupportedException"/>
        /// describing the issue. This limitation should also be documented by the journal plugin.
        /// 
        /// If there are failures when storing any of the messages in the batch the returned
        /// <see cref="Task"/> must be completed with failure. The <see cref="Task"/> must only be completed with
        /// success when all messages in the batch have been confirmed to be stored successfully,
        /// i.e. they will be readable, and visible, in a subsequent replay. If there is
        /// uncertainty about if the messages were stored or not the <see cref="Task"/> must be completed
        /// with failure.
        /// 
        /// Data store connection problems must be signaled by completing the <see cref="Task"/> with
        /// failure.
        /// 
        /// The journal can also signal that it rejects individual messages (<see cref="AtomicWrite"/>) by
        /// the returned <see cref="Task"/>. It is possible but not mandatory to reduce
        /// number of allocations by returning null for the happy path,
        /// i.e. when no messages are rejected. Otherwise the returned list must have as many elements
        /// as the input <paramref name="messages"/>. Each result element signals if the corresponding
        /// <see cref="AtomicWrite"/> is rejected or not, with an exception describing the problem. Rejecting
        /// a message means it was not stored, i.e. it must not be included in a later replay.
        /// Rejecting a message is typically done before attempting to store it, e.g. because of
        /// serialization error.
        /// 
        /// Data store connection problems must not be signaled as rejections.
        /// 
        /// It is possible but not mandatory to reduce number of allocations by returning
        /// null for the happy path, i.e. when no messages are rejected.
        /// 
        /// Calls to this method are serialized by the enclosing journal actor. If you spawn
        /// work in asynchronous tasks it is alright that they complete the futures in any order,
        /// but the actual writes for a specific persistenceId should be serialized to avoid
        /// issues such as events of a later write are visible to consumers (query side, or replay)
        /// before the events of an earlier write are visible.
        /// A <see cref="PersistentActor"/> will not send a new <see cref="WriteMessages"/> request before
        /// the previous one has been completed.
        /// 
        /// Please not that the <see cref="IPersistentRepresentation.Sender"/> of the contained
        /// <see cref="Persistent"/> objects has been nulled out (i.e. set to <see cref="ActorRefs.NoSender"/>
        /// in order to not use space in the journal for a sender reference that will likely be obsolete
        /// during replay.
        /// 
        /// Please also note that requests for the highest sequence number may be made concurrently
        /// to this call executing for the same `persistenceId`, in particular it is possible that
        /// a restarting actor tries to recover before its outstanding writes have completed.
        /// In the latter case it is highly desirable to defer reading the highest sequence number
        /// until all outstanding writes have completed, otherwise the <see cref="PersistentActor"/>
        /// may reuse sequence numbers.
        /// 
        /// This call is protected with a circuit-breaker.
        /// </summary>
        /// <param name="messages">TBD</param>
        /// <returns>TBD</returns>
        protected abstract Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages);

        /// <summary>
        /// Asynchronously deletes all persistent messages up to inclusive <paramref name="toSequenceNr"/>
        /// bound.
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        protected abstract Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr);

        /// <summary>
        /// Plugin API: Allows plugin implementers to use f.PipeTo(Self)
        /// and handle additional messages for implementing advanced features
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool ReceivePluginInternal(object message)
        {
            return false;
        }

        /// <inheritdoc/>
        protected sealed override bool Receive(object message)
        {
            return ReceiveWriteJournal(message) || ReceivePluginInternal(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected bool ReceiveWriteJournal(object message)
        {
            switch (message)
            {
                case WriteMessages writeMessages:
                    HandleWriteMessages(writeMessages);
                    return true;
                case ReplayMessages replayMessages:
                    HandleReplayMessages(replayMessages);
                    return true;
                case DeleteMessagesTo deleteMessagesTo:
                    HandleDeleteMessagesTo(deleteMessagesTo);
                    return true;
                default:
                    return false;
            }
        }

        private void HandleDeleteMessagesTo(DeleteMessagesTo message)
        {
            var eventStream = Context.System.EventStream;
            _breaker.WithCircuitBreaker(() => DeleteMessagesToAsync(message.PersistenceId, message.ToSequenceNr))
                .ContinueWith(t => !t.IsFaulted && !t.IsCanceled
                        ? new DeleteMessagesSuccess(message.ToSequenceNr) as object
                        : new DeleteMessagesFailure(
                            t.IsFaulted
                                ? TryUnwrapException(t.Exception)
                                : new OperationCanceledException(
                                    "DeleteMessagesToAsync canceled, possibly due to timing out."),
                            message.ToSequenceNr),
                    _continuationOptions)
                .PipeTo(message.PersistentActor)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted && !t.IsCanceled && CanPublish)
                        eventStream.Publish(message);
                }, _continuationOptions);
        }

        private void HandleReplayMessages(ReplayMessages message)
        {
            var replyTo = _isReplayFilterEnabled
                ? Context.ActorOf(ReplayFilter.Props(message.PersistentActor, _replayFilterMode, _replayFilterWindowSize,
                    _replayFilterMaxOldWriters, _replayDebugEnabled))
                : message.PersistentActor;

            var context = Context;
            var eventStream = Context.System.EventStream;

            var readHighestSequenceNrFrom = Math.Max(0L, message.FromSequenceNr - 1);
            var promise = new TaskCompletionSource<long>();
            _breaker.WithCircuitBreaker(() => ReadHighestSequenceNrAsync(message.PersistenceId, readHighestSequenceNrFrom))
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted && !t.IsCanceled)
                    {
                        var highSequenceNr = t.Result;
                        var toSequenceNr = Math.Min(message.ToSequenceNr, highSequenceNr);
                        if (toSequenceNr <= 0L || message.FromSequenceNr > toSequenceNr)
                        {
                            promise.SetResult(highSequenceNr);
                        }
                        else
                        {
                            // Send replayed messages and replay result to persistentActor directly. No need
                            // to resequence replayed messages relative to written and looped messages.
                            // not possible to use circuit breaker here
                            ReplayMessagesAsync(context, message.PersistenceId, message.FromSequenceNr, toSequenceNr, message.Max, p =>
                            {
                                if (!p.IsDeleted) // old records from pre 1.0.7 may still have the IsDeleted flag
                                {
                                    foreach (var adaptedRepresentation in AdaptFromJournal(p))
                                    {
                                        replyTo.Tell(new ReplayedMessage(adaptedRepresentation), ActorRefs.NoSender);
                                    }
                                }
                            })
                            .ContinueWith(replayTask =>
                            {
                                if (!replayTask.IsFaulted && !replayTask.IsCanceled)
                                    promise.SetResult(highSequenceNr);
                                else
                                    promise.SetException(replayTask.IsFaulted
                                        ? TryUnwrapException(replayTask.Exception)
                                        : new OperationCanceledException("ReplayMessagesAsync canceled, possibly due to timing out."));
                            }, _continuationOptions);
                        }
                    }
                    else
                    {
                        promise.SetException(t.IsFaulted
                            ? TryUnwrapException(t.Exception)
                            : new OperationCanceledException("ReadHighestSequenceNrAsync canceled, possibly due to timing out."));
                    }
                }, _continuationOptions);
            promise.Task
                .ContinueWith(t => !t.IsFaulted
                    ? new RecoverySuccess(t.Result) as IJournalResponse
                    : new ReplayMessagesFailure(TryUnwrapException(t.Exception)), _continuationOptions)
                .PipeTo(replyTo)
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted && CanPublish) eventStream.Publish(message);
                }, _continuationOptions);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        /// <returns>TBD</returns>
        protected Exception TryUnwrapException(Exception e)
        {
            var aggregateException = e as AggregateException;
            if (aggregateException != null)
            {
                aggregateException = aggregateException.Flatten();
                if (aggregateException.InnerExceptions.Count == 1)
                    return aggregateException.InnerExceptions[0];
            }
            return e;
        }

        private void HandleWriteMessages(WriteMessages message)
        {
            var counter = _resequencerCounter;

            /*
             * Self MUST BE CLOSED OVER here, or the code below will be subject to race conditions which may result
             * in failure, as the `IActorContext` needed for resolving Context.Self will be done outside the current
             * execution context.
             */
            var self = Self;
            _resequencerCounter += message.Messages.Aggregate(1, (acc, m) => acc + m.Size);
            var atomicWriteCount = message.Messages.OfType<AtomicWrite>().Count();
            AtomicWrite[] prepared;
            Task<IImmutableList<Exception>> writeResult;
            Exception writeMessagesAsyncException = null;
            try
            {
                prepared = PreparePersistentBatch(message.Messages).ToArray();
                // try in case AsyncWriteMessages throws
                try
                {
                    writeResult = _breaker.WithCircuitBreaker(() => WriteMessagesAsync(prepared));
                }
                catch (Exception e)
                {
                    writeResult = Task.FromResult((IImmutableList<Exception>) null);
                    writeMessagesAsyncException = e;
                }
            }
            catch (Exception e)
            {
                // exception from PreparePersistentBatch => rejected
                writeResult = Task.FromResult((IImmutableList<Exception>)Enumerable.Repeat(e, atomicWriteCount).ToImmutableList());
            }

            Action<Func<IPersistentRepresentation, Exception, object>, IImmutableList<Exception>> resequence = (mapper, results) =>
            {
                var i = 0;
                var enumerator = results != null ? results.GetEnumerator() : null;
                foreach (var resequencable in message.Messages)
                {
                    if (resequencable is AtomicWrite)
                    {
                        var aw = resequencable as AtomicWrite;
                        Exception exception = null;
                        if (enumerator != null)
                        {
                            enumerator.MoveNext();
                            exception = enumerator.Current;
                        }
                        foreach (var p in (IEnumerable<IPersistentRepresentation>)aw.Payload)
                        {
                            _resequencer.Tell(new Desequenced(mapper(p, exception), counter + i + 1, message.PersistentActor, p.Sender));
                            i++;
                        }
                    }
                    else
                    {
                        var loopMsg = new LoopMessageSuccess(resequencable.Payload, message.ActorInstanceId);
                        _resequencer.Tell(new Desequenced(loopMsg, counter + i + 1, message.PersistentActor,
                            resequencable.Sender));
                        i++;
                    }
                }
            };

            writeResult
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted && !t.IsCanceled && writeMessagesAsyncException == null)
                    {
                        if (t.Result != null && t.Result.Count != atomicWriteCount)
                            throw new IllegalStateException($"AsyncWriteMessages return invalid number or results. Expected [{atomicWriteCount}], but got [{t.Result.Count}].");

                        _resequencer.Tell(new Desequenced(WriteMessagesSuccessful.Instance, counter, message.PersistentActor, self));
                        resequence((x, exception) => exception == null
                            ? (object)new WriteMessageSuccess(x, message.ActorInstanceId)
                            : new WriteMessageRejected(x, exception, message.ActorInstanceId), t.Result);
                    }
                    else
                    {
                        var exception = writeMessagesAsyncException != null
                            ? writeMessagesAsyncException
                            : (t.IsFaulted
                                ? TryUnwrapException(t.Exception)
                                : new OperationCanceledException(
                                    "WriteMessagesAsync canceled, possibly due to timing out."));
                        _resequencer.Tell(new Desequenced(new WriteMessagesFailed(exception, atomicWriteCount), counter, message.PersistentActor, self));
                        resequence((x, _) => new WriteMessageFailure(x, exception, message.ActorInstanceId), null);
                    }
                }, _continuationOptions);
        }

        internal sealed class Desequenced
        {
            public Desequenced(object message, long sequenceNr, IActorRef target, IActorRef sender)
            {
                Message = message;
                SequenceNr = sequenceNr;
                Target = target;
                Sender = sender;
            }

            public object Message { get; }

            public long SequenceNr { get; }

            public IActorRef Target { get; }

            public IActorRef Sender { get; }
        }

        internal class Resequencer : ActorBase
        {
            private readonly IDictionary<long, Desequenced> _delayed = new Dictionary<long, Desequenced>();
            private long _delivered = 0L;

            protected override bool Receive(object message)
            {
                Desequenced d;
                if ((d = message as Desequenced) != null)
                {
                    do
                    {
                        d = Resequence(d);
                    } while (d != null);
                    return true;
                }
                return false;
            }

            private Desequenced Resequence(Desequenced desequenced)
            {
                if (desequenced.SequenceNr == _delivered + 1)
                {
                    _delivered = desequenced.SequenceNr;
                    desequenced.Target.Tell(desequenced.Message, desequenced.Sender);
                }
                else
                {
                    _delayed.Add(desequenced.SequenceNr, desequenced);
                }

                var delivered = _delivered + 1;
                if (_delayed.TryGetValue(delivered, out Desequenced d))
                {
                    _delayed.Remove(delivered);
                    return d;
                }

                return null;
            }
        }
    }
}
