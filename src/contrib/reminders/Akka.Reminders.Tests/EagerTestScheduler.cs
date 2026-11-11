using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;

namespace Akka.Reminders.Tests;

/// <summary>
/// A <see cref="TestScheduler"/> that can fire a zero-delay one-shot as soon as it is scheduled, the way a
/// real scheduler may when its tick thread runs straight after the call. It behaves like the plain
/// <see cref="TestScheduler"/> until <see cref="FireZeroDelayAtOnce"/> is set.
/// </summary>
public sealed class EagerTestScheduler : TestScheduler, IScheduler
{
    private int _firedAtOnce;

    public EagerTestScheduler(Config schedulerConfig, ILoggingAdapter log) : base(schedulerConfig, log)
    {
    }

    public volatile bool FireZeroDelayAtOnce;

    /// <summary>
    /// How many zero-delay one-shots were fired at once.
    /// </summary>
    public int FiredAtOnce => Volatile.Read(ref _firedAtOnce);

    public new void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender)
        => ScheduleTellOnce(delay, receiver, message, sender, null!);

    public new void ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, IActorRef sender, ICancelable cancelable)
    {
        if (FireZeroDelayAtOnce && delay <= TimeSpan.Zero)
        {
            Interlocked.Increment(ref _firedAtOnce);
            receiver.Tell(message, sender);
            return;
        }

        base.ScheduleTellOnce(delay, receiver, message, sender, cancelable);
    }
}
