using Akka.Actor;

namespace Akka.Reminders.Tests.Model;

/// <summary>
/// The reply-to ref of one harness call. Writes the reply to the <see cref="Journal"/> inside
/// <c>Tell</c>, on the thread of the actor that sends it. An <c>Ask</c> would let the caller write it
/// when it wakes up, after the scheduler may already have journaled its next delivery, and then the
/// journal order would not be the order things happened.
/// </summary>
internal sealed class JournalingReply<T> : MinimalActorRef
{
    private static long _count;

    private readonly Action<T>? _journal;
    private readonly TaskCompletionSource<T> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _answered;

    public JournalingReply(ActorSystem system, Action<T>? journal)
    {
        _journal = journal;
        Provider = ((ExtendedActorSystem)system).Provider;
        Path = new RootActorPath(new Address("akka", system.Name)) / $"harness-reply-{Interlocked.Increment(ref _count)}";
    }

    public override ActorPath Path { get; }

    public override IActorRefProvider Provider { get; }

    /// <summary>Completes after the reply is journaled.</summary>
    public Task<T> Reply => _reply.Task;

    protected override void TellInternal(object message, IActorRef sender)
    {
        if (Interlocked.Exchange(ref _answered, 1) == 1)
            return; // one answer per call
        switch (message)
        {
            case T reply:
                _journal?.Invoke(reply);
                _reply.TrySetResult(reply);
                break;
            case Status.Failure failure:
                _reply.TrySetException(failure.Cause);
                break;
            default:
                _reply.TrySetException(new ModelViolation($"The scheduler answered {message.GetType().Name}; the harness expected {typeof(T).Name}"));
                break;
        }
    }
}
