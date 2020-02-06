//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Annotations;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Implementation;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams
{
    [DoNotInherit]
    public abstract class Materializer : IMaterializer, IDisposable
    {
        #region Static
        
        // NOTE: From Scala source
        /// <summary>
        /// Implicitly provides the system wide materializer from a classic or typed `ActorSystem`
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public static IMaterializer MatFromSystem(IClassicActorSystemProvider provider) =>
            // TODO: This casting shouldn't be here - for some reason Scala does implicit conversion that I can't see
            new SystemMaterializer(provider.ClassicSystem as ExtendedActorSystem).Materializer;

        /// <summary>
        ///  Scala API: Create a materializer whose lifecycle will be tied to the one of the passed actor context.
        ///  When the actor stops the materializer will stop and all streams created with it will be failed with an [[AbruptTerminationExeption]]
        ///
        ///  You can pass either a classic actor context or a typed actor context.
        /// </summary>
        /// <param name="contextProvider"></param>
        /// <returns></returns>
        public static IMaterializer Apply(IClassicActorContextProvider contextProvider)
#pragma warning disable 619,618
            => ActorMaterializer.Create(contextProvider.ClassicActorContext, null, null);
#pragma warning restore 619,618

        /// <summary>
        /// Create a materializer whose lifecycle will be tied to the one of the passed actor context.
        /// When the actor stops the materializer will stop and all streams created with it will be failed with an [[AbruptTerminationExeption]]
        ///
        /// You can pass either a classic actor context or a typed actor context.
        /// </summary>
        /// <param name="contextProvider"></param>
        /// <returns></returns>
        public static IMaterializer Create(IClassicActorContextProvider contextProvider) => Apply(contextProvider);

        /// Create a new materializer that will stay alive as long as the system does or until it is explicitly stopped.
        /// 
        /// Note prefer using the default [[SystemMaterializer]] that is implicitly available if you have an implicit
        /// `ActorSystem` in scope. Only create new system level materializers if you have specific
        /// needs or want to test abrupt termination of a custom graph stage. If you want to tie the lifecycle
        /// of the materializer to an actor, use the factory that takes an [[ActorContext]] instead.
        
        public static IMaterializer Apply(IClassicActorSystemProvider systemProvider)
#pragma warning disable 612, 618
            => ActorMaterializer.Create(systemProvider.ClassicSystem, null, null);
#pragma warning restore 612, 618
        
        /// <summary>
        ///  Create a new materializer that will stay alive as long as the system does or until it is explicitly stopped.
        ///  
        ///  *Note* prefer using the default <see cref="SystemMaterializer"/> by passing the `ActorSystem` to the various `run`
        ///  methods on the streams. Only create new system level materializers if you have specific
        ///  needs or want to test abrupt termination of a custom graph stage. If you want to tie the
        ///  lifecycle of the materializer to an actor, use the factory that takes an <see cref="IActorContext"/> instead.
        /// </summary>
        /// <param name="systemProvider"></param>
        /// <returns></returns>
        public static IMaterializer Create(IClassicActorSystemProvider systemProvider) => Apply(systemProvider);
        
        #endregion
        
        /// <summary>
        /// Indicates if the materializer has been shut down.
        /// </summary>
        public abstract bool IsShutdown { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract ActorSystem System { get; }
        
        [InternalApi]
        public abstract IActorRef ActorOf(MaterializationContext context, Props props);
        
        /// <summary>
        /// Custom <see cref="GraphStage{TShape}"/>s that needs logging should use <see cref="IStageLogging"/>
        /// </summary>
        [InternalApi]
        public abstract ILoggingAdapter Logger { get; }

        [InternalApi]
        public abstract IActorRef Supervisor { get; }
        
        [Obsolete("Use attributes to access settings from stages")]
        public abstract ActorMaterializerSettings Settings { get; }
        
        public abstract MessageDispatcher ExecutionContext { get; }
        
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable);
        
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes defaultAttributes);
        
        public abstract ICancelable ScheduleOnce(TimeSpan delay, Action action);
        
        [Obsolete("Use ScheduleWithFixedDelay or ScheduleAtFixedRate instead.")]
        public virtual ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            return SchedulePeriodically(initialDelay, interval, action);
        }
        
        // Note: From Scala
        public abstract ICancelable ScheduleWithFixedDelay(TimeSpan initialDelay, TimeSpan delay, Action action);
        
        // Note: From Scala
        public abstract ICancelable ScheduleAtFixedRate(TimeSpan initialDelay, TimeSpan interval, Action action);
        
        // Note: From Scala
        [Obsolete("Use ScheduleWithFixedDelay or ScheduleAtFixedRate instead.")]
        public abstract ICancelable SchedulePeriodically(TimeSpan initialDelay, TimeSpan interval, Action action);
        
        /// <summary>
        /// Shuts down this materializer and all the stages that have been materialized through this materializer. After
        /// having shut down, this materializer cannot be used again. Any attempt to materialize stages after having
        /// shut down will result in an <see cref="IllegalStateException"/> being thrown at materialization time.
        /// </summary>
        public virtual void Shutdown() { }
        
        public virtual void Dispose() => Shutdown();
        
        /// <summary>
        /// The `namePrefix` shall be used for deriving the names of processing
        /// entities that are created during materialization. This is meant to aid
        /// logging and failure reporting both during materialization and while the
        /// stream is running. 
        /// </summary>
        /// <param name="name">Prefix to apply to the name of entities created during materialization</param>
        /// <returns></returns>
        public abstract IMaterializer WithNamePrefix(string name);

    }
}
