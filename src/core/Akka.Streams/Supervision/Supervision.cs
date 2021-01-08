//-----------------------------------------------------------------------
// <copyright file="Supervision.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Supervision
{
    /// <summary>
    /// TBD
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
    /// TBD
    /// </summary>
    /// <param name="cause">TBD</param>
    /// <returns>TBD</returns>
    public delegate Directive Decider(Exception cause);

    /// <summary>
    /// TBD
    /// </summary>
    public static class Deciders
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Decider StoppingDecider = cause => Directive.Stop;
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Decider ResumingDecider = cause => Directive.Resume;
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Decider RestartingDecider = cause => Directive.Restart;
    }
}
