//-----------------------------------------------------------------------
// <copyright file="Schedulers.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

[<AutoOpen>]
module Schedulers

open Akka.Actor
open System

type Akka.Actor.IActionScheduler with

    /// <summary>
    /// Schedules a function to be invoked repeatedly in the provided time intervals. 
    /// </summary>
    /// <param name="after">Initial delay to first function call.</param>
    /// <param name="every">Interval.</param>
    /// <param name="fn">Function called by the scheduler.</param>
    /// <param name="cancelable">Optional cancelation token</param>
    member this.ScheduleRepeatedly(after: TimeSpan, every: TimeSpan, fn: unit->unit, ?cancelable: ICancelable) : unit =
        let action = Action fn
        match cancelable with
        | Some c -> this.ScheduleRepeatedly(after, every, action, c)
        | None -> this.ScheduleRepeatedly(after, every, action)
        
    /// <summary>
    /// Schedules a single function call using specified sheduler.
    /// </summary>
    /// <param name="after">Delay before calling the function.</param>
    /// <param name="fn">Function called by the scheduler.</param>
    /// <param name="cancelable">Optional cancelation token</param>
    member this.ScheduleOnce(after: TimeSpan, fn: unit->unit, ?cancelable: ICancelable) : unit =
        let action = Action fn
        match cancelable with
        | Some c -> this.ScheduleOnce(after, action, c)
        | None -> this.ScheduleOnce(after, action)
        
type Akka.Actor.ITellScheduler with

    /// <summary>
    /// Schedules a <paramref name="message"/> to be sent to the provided <paramref name="receiver"/> in specified time intervals.
    /// </summary>
    /// <param name="after">Initial delay to first function call.</param>
    /// <param name="every">Interval.</param>
    /// <param name="message">Message to be sent to the receiver by the scheduler.</param>
    /// <param name="receiver">Message receiver.</param>
    member this.ScheduleTellRepeatedly(after: TimeSpan, every: TimeSpan, receiver: IActorRef, message: 'Message) : unit =
        this.ScheduleTellRepeatedly(after, every, receiver, message, ActorRefs.NoSender)
        
    /// <summary>
    /// Schedules a single <paramref name="message"/> send to the provided <paramref name="receiver"/>.
    /// </summary>
    /// <param name="after">Delay before sending a message.</param>
    /// <param name="message">Message to be sent to the receiver by the scheduler.</param>
    /// <param name="receiver">Message receiver.</param>
    member this.ScheduleTellOnce(after: TimeSpan, receiver: IActorRef, message: 'Message) : unit =
        this.ScheduleTellOnce(after, receiver, message, ActorRefs.NoSender)

