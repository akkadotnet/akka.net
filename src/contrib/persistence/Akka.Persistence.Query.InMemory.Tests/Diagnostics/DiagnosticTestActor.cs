// -----------------------------------------------------------------------
//  <copyright file="DiagnosticTestActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;

namespace Akka.Persistence.Query.InMemory.Tests.Diagnostics;

/// <summary>
/// A diagnostic version of the TCK TestActor that records timing information
/// at critical points in the actor lifecycle.
/// </summary>
public class DiagnosticTestActor : UntypedPersistentActor, IWithUnboundedStash
{
    public static Props Props(string persistenceId) =>
        Actor.Props.Create(() => new DiagnosticTestActor(persistenceId));

    public sealed class DeleteCommand
    {
        public DeleteCommand(long toSequenceNr)
        {
            ToSequenceNr = toSequenceNr;
        }

        public long ToSequenceNr { get; }
    }

    private readonly ILoggingAdapter _log;
    private readonly Stopwatch _lifecycleSw = new();

    public DiagnosticTestActor(string persistenceId)
    {
        PersistenceId = persistenceId;
        _log = Context.GetLogger();
    }

    public override string PersistenceId { get; }

    public new IStash Stash { get; set; } = null!;

    public override void AroundPreStart()
    {
        _lifecycleSw.Start();
        var threadId = Thread.CurrentThread.ManagedThreadId;

        DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] AroundPreStart ENTER on Thread {threadId}");
        _log.Debug("[{0}] AroundPreStart ENTER on Thread {1}", PersistenceId, threadId);

        // This is where the magic happens - base.AroundPreStart() will:
        // 1. Access Journal property (may trigger lazy init)
        // 2. Access SnapshotStore property (may trigger lazy init)
        // 3. Access RecoveryPermitter property (may trigger lazy init)
        // 4. Call RequestRecoveryPermit()

        DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] Calling base.AroundPreStart()");
        base.AroundPreStart();

        DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] AroundPreStart EXIT after {_lifecycleSw.ElapsedMilliseconds}ms on Thread {threadId}");
        _log.Debug("[{0}] AroundPreStart EXIT after {1}ms on Thread {2}", PersistenceId, _lifecycleSw.ElapsedMilliseconds, threadId);
    }

    protected override void OnRecover(object message)
    {
        var threadId = Thread.CurrentThread.ManagedThreadId;

        switch (message)
        {
            case RecoveryCompleted:
                DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] RecoveryCompleted after {_lifecycleSw.ElapsedMilliseconds}ms on Thread {threadId}");
                _log.Debug("[{0}] RecoveryCompleted after {1}ms on Thread {2}", PersistenceId, _lifecycleSw.ElapsedMilliseconds, threadId);
                break;
            default:
                DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] OnRecover: {message.GetType().Name} on Thread {threadId}");
                _log.Debug("[{0}] OnRecover received {1} [{2}]", PersistenceId, message, message.GetType());
                break;
        }
    }

    protected override void OnCommand(object message)
    {
        var threadId = Thread.CurrentThread.ManagedThreadId;

        switch (message)
        {
            case DeleteCommand delete:
                DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] DeleteCommand received on Thread {threadId}");
                DeleteMessages(delete.ToSequenceNr);
                Become(WhileDeleting(Sender));
                break;
            case string cmd:
                DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] Command '{cmd}' received on Thread {threadId}");
                var sender = Sender;
                Persist(cmd, e =>
                {
                    DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] Message '{e}' persisted on Thread {Thread.CurrentThread.ManagedThreadId}");
                    _log.Debug("[{0}] Message {1} persisted", PersistenceId, e);
                    sender.Tell($"{e}-done");
                });
                break;
        }
    }

    protected Receive WhileDeleting(IActorRef originalSender)
    {
        return message =>
        {
            switch (message)
            {
                case DeleteMessagesSuccess success:
                    originalSender.Tell($"{success.ToSequenceNr}-deleted");
                    Become(OnCommand);
                    Stash.UnstashAll();
                    break;
                case DeleteMessagesFailure failure:
                    Log.Error(failure.Cause, "Failed to delete messages to sequence number [{0}].", failure.ToSequenceNr);
                    originalSender.Tell($"{failure.ToSequenceNr}-deleted-failed");
                    Become(OnCommand);
                    Stash.UnstashAll();
                    break;
                default:
                    Stash.Stash();
                    break;
            }

            return true;
        };
    }

    protected override void PreStart()
    {
        DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] PreStart on Thread {Thread.CurrentThread.ManagedThreadId}");
        base.PreStart();
    }

    protected override void PostStop()
    {
        DiagnosticTimeline.Record("Actor", $"[{PersistenceId}] PostStop after {_lifecycleSw.ElapsedMilliseconds}ms on Thread {Thread.CurrentThread.ManagedThreadId}");
        base.PostStop();
    }
}

/// <summary>
/// Color/fruit tagger for tagged events (same as TCK version).
/// </summary>
public class DiagnosticColorFruitTagger : IWriteEventAdapter
{
    public static IImmutableSet<string> Colors { get; } = ImmutableHashSet.Create("green", "black", "blue");
    public static IImmutableSet<string> Fruits { get; } = ImmutableHashSet.Create("apple", "banana");

    public string Manifest(object evt) => string.Empty;

    public object ToJournal(object evt)
    {
        if (evt is string s)
        {
            var colorTags = Colors.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
            var fruitTags = Fruits.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
            var tags = colorTags.Union(fruitTags);
            return tags.IsEmpty
                ? evt
                : new Tagged(evt, tags);
        }

        return evt;
    }
}
