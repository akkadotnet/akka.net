//-----------------------------------------------------------------------
// <copyright file="IMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Streams
{
    public interface IMaterializer
    {
        /// <summary>
        /// The <paramref name="namePrefix"/> shall be used for deriving the names of processing
        /// entities that are created during materialization. This is meant to aid
        /// logging and failure reporting both during materialization and while the
        /// stream is running.
        /// </summary>
        IMaterializer WithNamePrefix(string namePrefix);

        /// <summary>
        /// This method interprets the given Flow description and creates the running
        /// stream. The result can be highly implementation specific, ranging from
        /// local actor chains to remote-deployed processing networks.
        /// </summary>
        TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable);

        /// <summary>
        /// Interface for stages that need timer services for their functionality. Schedules a
        /// single task with the given delay.
        /// </summary>
        /// <returns>
        /// A <see cref="ICancelable"/> that allows cancelling the timer. Cancelling is best effort, 
        /// if the event has been already enqueued it will not have an effect.
        /// </returns>
        ICancelable ScheduleOnce(TimeSpan delay, Action action);

        /// <summary>
        /// Interface for stages that need timer services for their functionality. Schedules a
        /// repeated task with the given interval between invocations.
        /// </summary>
        /// <returns>
        /// A <see cref="ICancelable"/> that allows cancelling the timer. Cancelling is best effort, 
        /// if the event has been already enqueued it will not have an effect.
        /// </returns>
        ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);

        /// <summary>
        /// Running a flow graph will require execution resources, as will computations
        /// within Sources, Sinks, etc. This <see cref="MessageDispatcher"/>
        /// can be used by parts of the flow to submit processing jobs for execution,
        /// run Future callbacks, etc.
        /// </summary>
        MessageDispatcher ExecutionContext { get; }
    }

    public sealed class NoMaterializer : IMaterializer
    {
        public static readonly IMaterializer Instance = new NoMaterializer();
        private NoMaterializer() { }

        public IMaterializer WithNamePrefix(string name)
        {
            throw new NotSupportedException("NoMaterializer cannot be named");
        }

        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
        {
            throw new NotSupportedException("NoMaterializer cannot materialize");
        }

        public ICancelable ScheduleOnce(TimeSpan delay, Action action)
        {
            throw new NotSupportedException("NoMaterializer cannot schedule a single event");
        }

        public ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            throw new NotSupportedException("NoMaterializer cannot schedule a repeated event");
        }

        public MessageDispatcher ExecutionContext
        {
            get { throw new NotSupportedException("NoMaterializer doesn't provide an ExecutionContext"); }
        }
    }

    /// <summary>
    /// Context parameter to the create methods of sources and sinks.
    /// </summary>
    public struct MaterializationContext
    {
        public readonly IMaterializer Materializer;
        public readonly Attributes EffectiveAttributes;
        public readonly string StageName;

        public MaterializationContext(IMaterializer materializer, Attributes effectiveAttributes, string stageName)
        {
            Materializer = materializer;
            EffectiveAttributes = effectiveAttributes;
            StageName = stageName;
        }
    }
}