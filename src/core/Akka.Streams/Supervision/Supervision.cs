using System;

namespace Akka.Streams.Supervision
{
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

    public delegate Directive Decider(Exception cause);

    public static class Deciders
    {
        public static readonly Decider StoppingDecider = cause => Directive.Stop;
        public static readonly Decider ResumingDecider = cause => Directive.Resume;
        public static readonly Decider RestartingDecider = cause => Directive.Restart;
    }
}