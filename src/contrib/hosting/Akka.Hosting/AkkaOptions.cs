// -----------------------------------------------------------------------
//  <copyright file="AkkaOptions.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Actor;
using Akka.Event;

namespace Akka.Hosting;

public enum TriStateValue
{
    None,
    All,
    Some
}

public class DeadLetterOptions
{
    /// <summary>
    /// Flag to indicate if dead letter should be logged, or published to <see cref="ActorSystem.EventStream"/> as
    /// <see cref="DeadLetter"/>, <see cref="Dropped"/>, or <see cref="UnhandledMessage"/>.
    /// If set to <see cref="TriStateValue.Some"/>, only the first N number of dead letters will be logged,
    /// where N is equal to <see cref="LogCount"/>.
    /// </summary>
    public TriStateValue ShouldLog { get; set; } = TriStateValue.Some;
    
    /// <summary>
    /// Number of dead letter messages to be logged. Only effective if <see cref="ShouldLog"/> is set to
    /// <see cref="TriStateValue.Some"/>
    /// </summary>
    public int? LogCount { get; set; }
    
    /// <summary>
    /// Flag to indicate that log letters should not be logged while the <see cref="ActorSystem"/> is shutting down.
    /// </summary>
    public bool? LogDuringShutdown { get; set; }
    
    /// <summary>
    /// Time delay to re-enable dead letter logging when <see cref="ShouldLog"/> is set to
    /// <see cref="TriStateValue.Some"/>. Suspends logging forever if set to less than or equal to 0, 
    /// </summary>
    public TimeSpan? LogSuspendDuration { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        if (ShouldLog is not TriStateValue.Some)
        {
            switch (ShouldLog)
            {
                case TriStateValue.All:
                    sb.AppendLine("log-dead-letters = on");
                    break;
                case TriStateValue.None:
                    sb.AppendLine("log-dead-letters = off");
                    break;
                default:
                    throw new IndexOutOfRangeException($"Unknown TriStateValue: {ShouldLog}");
            }
        } else if(LogCount is not null)
            sb.AppendLine($"log-dead-letters = {LogCount.Value}");
        
        if (LogDuringShutdown is not null)
            sb.AppendLine($"log-dead-letters-during-shutdown = {LogDuringShutdown.ToHocon()}");
        if (LogSuspendDuration is not null)
            sb.AppendLine($"log-dead-letters-suspend-duration = {LogSuspendDuration.ToHocon(allowInfinite: true, zeroIsInfinite: true)}");
        
        if(sb.Length == 0)
            return string.Empty;

        sb.Insert(0, "akka {");
        sb.AppendLine("}");
        return sb.ToString();
    }
}

public class DebugOptions
{
    /// <summary>
    /// Enable logging of any received message at <see cref="LogLevel.DebugLevel"/> level.
    /// </summary>
    public bool? Receive { get; set; }
    
    /// <summary>
    /// Enable <see cref="LogLevel.DebugLevel"/> logging of all <see cref="IAutoReceivedMessage"/> messages
    /// (for example, <see cref="Kill"/>, <see cref="PoisonPill"/>, etc).
    /// </summary>
    public bool? AutoReceive { get; set; }
    
    /// <summary>
    /// Enable <see cref="LogLevel.DebugLevel"/> logging of actor lifecycle changes
    /// </summary>
    public bool? LifeCycle { get; set; }
    
    /// <summary>
    /// Enable <see cref="LogLevel.DebugLevel"/> logging of <see cref="FSM{TState,TData}"/> for events, transitions
    /// and timers.
    /// </summary>
    public bool? FiniteStateMachine { get; set; }
    
    /// <summary>
    /// Enable <see cref="LogLevel.DebugLevel"/> logging of subscription changes on the
    /// <see cref="ActorSystem.EventStream"/>
    /// </summary>
    public bool? EventStream { get; set; }
    
    /// <summary>
    /// Enable <see cref="LogLevel.DebugLevel"/> logging of unhandled messages
    /// </summary>
    public bool? Unhandled { get; set; }
    
    /// <summary>
    /// Enable <see cref="LogLevel.DebugLevel"/> logging of misconfigured routers
    /// </summary>
    public bool? RouterMisconfiguration { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Receive is { })
            sb.AppendLine($"receive = {Receive.ToHocon()}");
        if(AutoReceive is { })
            sb.AppendLine($"autoreceive = {AutoReceive.ToHocon()}");
        if (LifeCycle is { })
            sb.AppendLine($"lifecycle = {LifeCycle.ToHocon()}");
        if (FiniteStateMachine is { })
            sb.AppendLine($"fsm = {FiniteStateMachine.ToHocon()}");
        if (EventStream is { })
            sb.AppendLine($"event-stream = {EventStream.ToHocon()}");
        if (Unhandled is { })
            sb.AppendLine($"unhandled = {Unhandled.ToHocon()}");
        if (RouterMisconfiguration is { })
            sb.AppendLine($"router-misconfiguration = {RouterMisconfiguration.ToHocon()}");
        
        if(sb.Length == 0)
            return string.Empty;

        sb.Insert(0, "akka.actor.debug {");
        sb.AppendLine("}");
        return sb.ToString();
    }
}