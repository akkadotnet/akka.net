//-----------------------------------------------------------------------
// <copyright file="SteppingMemoryJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Tests.Journal
{
    /// <summary>
    /// An in memory journal that will not complete any Persists or PersistAsyncs until it gets tokens
    /// to trigger those steps. Allows for tests that need to deterministically trigger the callbacks
    /// intermixed with receiving messages.
    /// 
    /// Configure your actor system using <see cref="SteppingMemoryJournal.Config(string)"/> and then access
    /// it using <see cref="SteppingMemoryJournal.GetRef(string)"/> send it <see cref="Token"/>s to
    /// allow one journal operation to complete.
    /// </summary>
    public sealed class SteppingMemoryJournal : MemoryJournal
    {
        /// <summary>
        /// Allow the journal to do one operation
        /// </summary>
        internal class Token
        {
            public static readonly Token Instance = new Token();
            private Token() { }
        }

        internal class TokenConsumed
        {
            public static readonly TokenConsumed Instance = new TokenConsumed();
            private TokenConsumed() { }
        }

        private static readonly TaskContinuationOptions _continuationOptions = TaskContinuationOptions.ExecuteSynchronously;
        // keep it in a thread safe global so that tests can get their hand on the actor ref and send Steps to it
        private static readonly ConcurrentDictionary<string, IActorRef> _current = new ConcurrentDictionary<string, IActorRef>();
        private readonly string _instanceId;
        private readonly Queue<Func<Task>> _queuedOps = new Queue<Func<Task>>();
        private readonly Queue<IActorRef> _queuedTokenRecipients = new Queue<IActorRef>();


        public SteppingMemoryJournal()
        {
            _instanceId = Context.System.Settings.Config.GetString("akka.persistence.journal.stepping-inmem.instance-id", null);
        }

        public static void Step(IActorRef journal)
        {
            journal.Ask(Token.Instance, TimeSpan.FromSeconds(3)).Wait();
        }

        public static Config Config(string instanceId)
        {
            return ConfigurationFactory.ParseString(@"
akka.persistence.journal.stepping-inmem.class="""+ typeof(SteppingMemoryJournal).FullName + @", Akka.Persistence.Tests""
akka.persistence.journal.plugin = ""akka.persistence.journal.stepping-inmem""
akka.persistence.journal.stepping-inmem.plugin-dispatcher = ""akka.actor.default-dispatcher""
akka.persistence.journal.stepping-inmem.instance-id = """ + instanceId + @"""");
        }

        /// <summary>
        /// Get the actor ref to the journal for a given instance id, throws exception if not found.
        /// </summary>
        public static IActorRef GetRef(string instanceId)
        {
            return _current[instanceId];
        }

        protected override bool ReceivePluginInternal(object message)
        {
            if (base.ReceivePluginInternal(message))
                return true;
            if (message is Token)
            {
                if (_queuedOps.Count == 0)
                    _queuedTokenRecipients.Enqueue(Sender);
                else
                {
                    var op = _queuedOps.Dequeue();
                    var tokenConsumer = Sender;
                    op().ContinueWith(t => tokenConsumer.Tell(TokenConsumed.Instance), _continuationOptions).Wait();
                }
                return true;
            }
            return false;
        }

        protected override void PreStart()
        {
            _current.AddOrUpdate(_instanceId, id => Self, (id, old) => Self);
            base.PreStart();
        }

        protected override void PostStop()
        {
            base.PostStop();
            IActorRef foo;
            _current.TryRemove(_instanceId, out foo);
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var tasks = messages.Select(message =>
            {
                return
                    WrapAndDoOrEnqueue(
                        () =>
                            base.WriteMessagesAsync(new[] {message})
                                .ContinueWith(t => t.Result != null ? t.Result.FirstOrDefault() : null,
                                    _continuationOptions | TaskContinuationOptions.OnlyOnRanToCompletion));
            });

            return Task.WhenAll(tasks)
                .ContinueWith(t => (IImmutableList<Exception>)t.Result.ToImmutableList(),
                    _continuationOptions | TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            return
                WrapAndDoOrEnqueue(
                    () =>
                        base.DeleteMessagesToAsync(persistenceId, toSequenceNr)
                            .ContinueWith(t => new object(),
                                _continuationOptions | TaskContinuationOptions.OnlyOnRanToCompletion));
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return WrapAndDoOrEnqueue(() => base.ReadHighestSequenceNrAsync(persistenceId, fromSequenceNr));
        }

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr, long toSequenceNr, long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            return
                WrapAndDoOrEnqueue(
                    () =>
                        base.ReplayMessagesAsync(context, persistenceId, fromSequenceNr, toSequenceNr, max,
                            recoveryCallback)
                            .ContinueWith(t => new object(),
                                _continuationOptions | TaskContinuationOptions.OnlyOnRanToCompletion));
        }

        private Task<T> WrapAndDoOrEnqueue<T>(Func<Task<T>> f)
        {
            var promise = new TaskCompletionSource<T>();
            var task = promise.Task;
            DoOrEnqueue(() =>
            {
                f()
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        promise.SetException(t.Exception);
                    else if (t.IsCanceled)
                        promise.SetCanceled();
                    else
                        promise.SetResult(t.Result);
                }, _continuationOptions);
                return task;
            });
            return task;
        }

        private void DoOrEnqueue(Func<Task> op)
        {
            if (_queuedTokenRecipients.Count > 0)
            {
                var completed = op();
                var tokenRecipient = _queuedTokenRecipients.Dequeue();
                completed.ContinueWith(t => tokenRecipient.Tell(TokenConsumed.Instance), _continuationOptions).Wait();
            }
            else
                _queuedOps.Enqueue(op);
        }
    }
}
