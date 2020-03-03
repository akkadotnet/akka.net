//-----------------------------------------------------------------------
// <copyright file="IMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Streams
{
    /// <summary>
    /// Materializer SPI (Service Provider Interface) 
    /// 
    /// Custom materializer implementations should be aware that the materializer SPI
    /// is not yet final and may change in patch releases of Akka. Please note that this
    /// does not impact end-users of Akka streams, only implementors of custom materializers,
    /// with whom the Akka.Net team co-ordinates such changes.
    /// 
    /// Once the SPI is final this notice will be removed.
    /// </summary>
    public interface IMaterializer
    {
        /// <summary>
        /// The <paramref name="namePrefix"/> shall be used for deriving the names of processing
        /// entities that are created during materialization. This is meant to aid
        /// logging and failure reporting both during materialization and while the
        /// stream is running.
        /// </summary>
        /// <param name="namePrefix">TBD</param>
        /// <returns>TBD</returns>
        IMaterializer WithNamePrefix(string namePrefix);

        /// <summary>
        /// This method interprets the given Flow description and creates the running
        /// stream. The result can be highly implementation specific, ranging from
        /// local actor chains to remote-deployed processing networks.
        /// </summary>
        /// <typeparam name="TMat">The type of the materialized value</typeparam>
        /// <param name="runnable">The flow that should be materialized.</param>
        /// <returns>The materialized value</returns>
        TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable);

        /// <summary>
        /// This method interprets the given Flow description and creates the running
        /// stream using an explicitly provided <see cref="Attributes"/> as top level attributes.
        /// stream. The result can be highly implementation specific, ranging from
        /// local actor chains to remote-deployed processing networks.
        /// </summary>
        /// <typeparam name="TMat">The type of the materialized value</typeparam>
        /// <param name="runnable">The flow that should be materialized.</param>
        /// <param name="initialAttributes">The initialAttributes for this materialization</param>
        /// <returns>The materialized value</returns>
        TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes);

        /// <summary>
        /// Interface for stages that need timer services for their functionality. Schedules a
        /// single task with the given delay.
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>
        /// A <see cref="ICancelable"/> that allows cancelling the timer. Cancelling is best effort, 
        /// if the event has been already enqueued it will not have an effect.
        /// </returns>
        ICancelable ScheduleOnce(TimeSpan delay, Action action);

        /// <summary>
        /// Interface for stages that need timer services for their functionality. Schedules a
        /// repeated task with the given interval between invocations.
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
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

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class NoMaterializer : IMaterializer
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly IMaterializer Instance = new NoMaterializer();
        private NoMaterializer() { }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="name">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoMaterializer"/> cannot be named.
        /// </exception>
        /// <returns>N/A</returns>
        public IMaterializer WithNamePrefix(string name)
        {
            throw new NotSupportedException("NoMaterializer cannot be named");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <typeparam name="TMat">N/A</typeparam>
        /// <param name="runnable">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoMaterializer"/> cannot be materialized.
        /// </exception>
        /// <returns>N/A</returns>
        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable)
        {
            throw new NotSupportedException("NoMaterializer cannot materialize");
        }


        /// <summary>
        /// N/A
        /// </summary>
        /// <typeparam name="TMat">N/A</typeparam>
        /// <param name="runnable">N/A</param>
        /// <param name="initialAttributes">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoMaterializer"/> cannot be materialized.
        /// </exception>
        /// <returns>N/A</returns>
        public TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes)
        {
            throw new NotSupportedException("NoMaterializer cannot materialize");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="delay">N/A</param>
        /// <param name="action">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoMaterializer"/> cannot schedule an event.
        /// </exception>
        /// <returns>N/A</returns>
        public ICancelable ScheduleOnce(TimeSpan delay, Action action)
        {
            throw new NotSupportedException("NoMaterializer cannot schedule a single event");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <param name="initialDelay">N/A</param>
        /// <param name="interval">N/A</param>
        /// <param name="action">N/A</param>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoMaterializer"/> cannot schedule a repeatable event.
        /// </exception>
        /// <returns>N/A</returns>
        public ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            throw new NotSupportedException("NoMaterializer cannot schedule a repeated event");
        }

        /// <summary>
        /// N/A
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// This exception is automatically thrown since <see cref="NoMaterializer"/> does not provide an execution context.
        /// </exception>
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
        /// <summary>
        /// TBD
        /// </summary>
        public readonly IMaterializer Materializer;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Attributes EffectiveAttributes;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string StageName;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="materializer">TBD</param>
        /// <param name="effectiveAttributes">TBD</param>
        /// <param name="stageName">TBD</param>
        public MaterializationContext(IMaterializer materializer, Attributes effectiveAttributes, string stageName)
        {
            Materializer = materializer;
            EffectiveAttributes = effectiveAttributes;
            StageName = stageName;
        }
    }
}
