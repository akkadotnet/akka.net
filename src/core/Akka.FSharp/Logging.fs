//-----------------------------------------------------------------------
// <copyright file="Logging.fs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
[<AutoOpen>]
module Akka.FSharp.Logging

open Akka.Event

/// Logs a message using configured Akka logger.
let log (level : LogLevel) (mailbox : Actor<'Message>) (msg : string) : unit = 
    let logger = mailbox.Log.Force()
    logger.Log(level, msg)

/// Logs a message at Debug level using configured Akka logger.
let inline logDebug mailbox msg = log LogLevel.DebugLevel mailbox msg

/// Logs a message at Info level using configured Akka logger.
let inline logInfo mailbox msg = log LogLevel.InfoLevel mailbox msg

/// Logs a message at Warning level using configured Akka logger.
let inline logWarning mailbox msg = log LogLevel.WarningLevel mailbox msg

/// Logs a message at Error level using configured Akka logger. 
let inline logError mailbox msg = log LogLevel.ErrorLevel mailbox msg

/// Logs an exception message at Error level using configured Akka logger.
let inline logException mailbox (e : exn) = log LogLevel.ErrorLevel mailbox (e.Message)

open Printf

let inline private doLogf level (mailbox : Actor<'Message>) msg = mailbox.Log.Value.Log(level, msg) |> ignore

/// Logs a message using configured Akka logger.
let inline logf (level : LogLevel) (mailbox : Actor<'Message>) = kprintf (doLogf level mailbox)

/// Logs a message at Debug level using configured Akka logger.
let inline logDebugf mailbox = kprintf (doLogf LogLevel.DebugLevel mailbox)

/// Logs a message at Info level using configured Akka logger.
let inline logInfof mailbox = kprintf (doLogf LogLevel.InfoLevel mailbox)

/// Logs a message at Warning level using configured Akka logger.
let inline logWarningf mailbox = kprintf (doLogf LogLevel.WarningLevel mailbox)

/// Logs a message at Error level using configured Akka logger. 
let inline logErrorf mailbox = kprintf (doLogf LogLevel.ErrorLevel mailbox)
