//-----------------------------------------------------------------------
// <copyright file="Schedulers.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    /// <param name="sender">Optional actor reference set up as message sender</param>
    /// <param name="cancelable">Optional cancelation token</param>
    member this.ScheduleTellRepeatedly(after: TimeSpan, every: TimeSpan, receiver: IActorRef, message: 'Message, ?sender: IActorRef, ?cancelable: ICancelable) : unit =
        let s = match sender with
                | Some aref -> aref
                | None -> ActorCell.GetCurrentSelfOrNoSender()
        match cancelable with
        | Some c -> this.ScheduleTellRepeatedly(after, every, receiver, message, s, c)
        | None -> this.ScheduleTellRepeatedly(after, every, receiver, message, s)
        
    /// <summary>
    /// Schedules a single <paramref name="message"/> send to the provided <paramref name="receiver"/>.
    /// </summary>
    /// <param name="after">Delay before sending a message.</param>
    /// <param name="message">Message to be sent to the receiver by the scheduler.</param>
    /// <param name="receiver">Message receiver.</param>
    /// <param name="sender">Optional actor reference set up as message sender</param>
    /// <param name="cancelable">Optional cancelation token</param>
    member this.ScheduleTellOnce(after: TimeSpan, receiver: IActorRef, message: 'Message, ?sender: IActorRef, ?cancelable: ICancelable) : unit =
        let s = match sender with
                | Some aref -> aref
                | None -> ActorCell.GetCurrentSelfOrNoSender()
        match cancelable with
        | Some c -> this.ScheduleTellOnce(after, receiver, message, s, c)
        | None -> this.ScheduleTellOnce(after, receiver, message, s)

