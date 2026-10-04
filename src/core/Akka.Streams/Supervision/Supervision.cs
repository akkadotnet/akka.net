//-----------------------------------------------------------------------
// <copyright file="Supervision.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Supervision
{
    /// <summary>
    /// Action a stream stage takes when its decider handles an exception from element processing.
    /// </summary>
    public enum Directive
    {
        /// <summary>
        /// The stream will be completed with failure if application code for processing an element throws an exception..
        /// </summary>
        Stop,

        /// <summary>
        /// The element is dropped and the stream continues if application code for processing an element throws an exception.
        /// </summary>
        Resume,

        /// <summary>
        /// The element is dropped and the stream continues after restarting the stage if application code for processing 
        /// an element throws an exception. Restarting a stage means that any accumulated state is cleared. 
        /// This is typically performed by creating a new instance of the stage.
        /// </summary>
        Restart
    }

    /// <summary>
    /// Maps an element-processing exception to a supervision directive.
    /// </summary>
    /// <param name="cause">Exception thrown while processing a stream element.</param>
    /// <returns>The directive that determines how the stream stage handles the exception.</returns>
    public delegate Directive Decider(Exception cause);

    /// <summary>
    /// Predefined supervision deciders for stopping, resuming, or restarting a stage.
    /// </summary>
    public static class Deciders
    {
        /// <summary>
        /// Returns <see cref="Directive.Stop"/> for any exception; a stage that supports supervision applies the directive.
        /// </summary>
        public static readonly Decider StoppingDecider = _ => Directive.Stop;
        /// <summary>
        /// Returns <see cref="Directive.Resume"/> for any exception; a stage that supports supervision applies the directive.
        /// </summary>
        public static readonly Decider ResumingDecider = _ => Directive.Resume;
        /// <summary>
        /// Returns <see cref="Directive.Restart"/> for any exception; a stage that supports supervision applies the directive.
        /// </summary>
        public static readonly Decider RestartingDecider = _ => Directive.Restart;
    }
}
