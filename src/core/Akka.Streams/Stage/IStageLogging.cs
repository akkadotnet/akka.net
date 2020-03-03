//-----------------------------------------------------------------------
// <copyright file="IStageLogging.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using Akka.Event;

namespace Akka.Streams.Stage
{
    /// <summary>
    /// Simple way to obtain a <see cref="ILoggingAdapter"/> when used together with an <see cref="ActorMaterializer"/>.
    /// If used with a different materializer <see cref="NoLogger"/> will be returned.
    ///
    /// Make sure to only access `Log` from GraphStage callbacks (such as `Pull`, `Push` or the async-callback).
    ///
    /// Note, abiding to <see cref="Attributes.LogLevels"/> has to be done manually,
    /// the logger itself is configured based on the logSource provided to it. Also, the `Log`
    /// itself would not know if you're calling it from a "on element" context or not, which is why
    /// these decisions have to be handled by the stage itself.
    /// </summary>
    public interface IStageLogging 
    {
        ILoggingAdapter Log { get; }
    }
}
