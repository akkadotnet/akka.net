//-----------------------------------------------------------------------
// <copyright file="ActorSystem.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    ///     An actor system is a hierarchical group of actors which share common
    ///     configuration, e.g. dispatchers, deployments, remote capabilities and
    ///     addresses. It is also the entry point for creating or looking up actors.
    ///     There are several possibilities for creating actors (see <see cref="Akka.Actor.Props"/>
    ///     for details on `props`):
    ///     <code>
    /// system.ActorOf(props, "name");
    /// system.ActorOf(props);
    /// system.ActorOf(Props.Create(typeof(MyActor)), "name");
    /// system.ActorOf(Props.Create(() =&gt; new MyActor(arg1, arg2), "name");
    /// </code>
    ///     Where no name is given explicitly, one will be automatically generated.
    ///     <b>
    ///         <i>Important Notice:</i>
    ///     </b>
    ///     This class is not meant to be extended by user code.
    /// </summary>
    public abstract class ActorSystem : IActorRefFactory, IDisposable
    {
        /// <summary>Gets the settings.</summary>
        /// <value>The settings.</value>
        public abstract Settings Settings { get; }

        /// <summary>Gets the name of this system.</summary>
        /// <value>The name.</value>
        public abstract string Name { get; }

        /// <summary>Gets the serialization.</summary>
        /// <value>The serialization.</value>
        public abstract Serialization.Serialization Serialization { get; }

        /// <summary>Gets the event stream.</summary>
        /// <value>The event stream.</value>
        public abstract EventStream EventStream { get; }

        /// <summary>
        ///     Gets the dead letters.
        /// </summary>
        /// <value>The dead letters.</value>
        public abstract IActorRef DeadLetters { get; }

        /// <summary>Gets the dispatchers.</summary>
        /// <value>The dispatchers.</value>
        public abstract Dispatchers Dispatchers { get; }

        /// <summary>Gets the mailboxes.</summary>
        /// <value>The mailboxes.</value>
        public abstract Mailboxes Mailboxes { get; }


        /// <summary>Gets the scheduler.</summary>
        /// <value>The scheduler.</value>
        public abstract IScheduler Scheduler { get; }

        /// <summary>Gets the log</summary>
        public abstract ILoggingAdapter Log { get; }

        /// <summary>
        /// Start-up time since the epoch.
        /// </summary>
        public TimeSpan StartTime { get; } = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Up-time of this actor system.
        /// </summary>
        public TimeSpan Uptime => MonotonicClock.ElapsedHighRes - _creationTime;

        private readonly TimeSpan _creationTime = MonotonicClock.ElapsedHighRes;

        /// <summary>
        /// Creates a new <see cref="ActorSystem"/> with the specified name and configuration.
        /// </summary>
        /// <param name="name">The name of the actor system to create. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <param name="config">The configuration used to create the actor system</param>
        /// <returns>A newly created actor system with the given name and configuration.</returns>
        public static ActorSystem Create(string name, Config config)
        {
            // var withFallback = config.WithFallback(ConfigurationFactory.Default());
            return CreateAndStartSystem(name, config);
        }

        /// <summary>
        /// Creates a new <see cref="ActorSystem"/> with the specified name.
        /// </summary>
        /// <param name="name">The name of the actor system to create. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <returns>A newly created actor system with the given name.</returns>
        public static ActorSystem Create(string name)
        {
            return CreateAndStartSystem(name, ConfigurationFactory.Load());
        }

        private static ActorSystem CreateAndStartSystem(string name, Config withFallback)
        {
            var system = new ActorSystemImpl(name, withFallback);
            system.Start();
            return system;
        }

        /// <summary>
        /// Retrieves the specified extension that is registered to this actor system.
        /// </summary>
        /// <param name="extensionId">The extension to retrieve</param>
        /// <returns>The specified extension registered to this actor system</returns>
        public abstract object GetExtension(IExtensionId extensionId);

        /// <summary>
        /// Retrieves an extension with the specified type that is registered to this actor system.
        /// </summary>
        /// <typeparam name="T">The type of extension to retrieve</typeparam>
        /// <returns>The specified extension registered to this actor system</returns>
        public abstract T GetExtension<T>() where T : class, IExtension;

        /// <summary>
        /// Determines whether this actor system has an extension with the specified type.
        /// </summary>
        /// <param name="type">The type of the extension being queried</param>
        /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
        public abstract bool HasExtension(Type type);

        /// <summary>
        /// Determines whether this actor system has the specified extension.
        /// </summary>
        /// <typeparam name="T">The type of the extension being queried</typeparam>
        /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
        public abstract bool HasExtension<T>() where T : class, IExtension;

        /// <summary>
        /// Tries to retrieve an extension with the specified type.
        /// </summary>
        /// <param name="extensionType">The type of extension to retrieve</param>
        /// <param name="extension">The extension that is retrieved if successful</param>
        /// <returns><c>true</c> if the retrieval was successful; otherwise <c>false</c>.</returns>
        public abstract bool TryGetExtension(Type extensionType, out object extension);

        /// <summary>
        /// Tries to retrieve an extension with the specified type
        /// </summary>
        /// <typeparam name="T">The type of extension to retrieve</typeparam>
        /// <param name="extension">The extension that is retrieved if successful</param>
        /// <returns><c>true</c> if the retrieval was successful; otherwise <c>false</c>.</returns>
        public abstract bool TryGetExtension<T>(out T extension) where T : class, IExtension;

        /// <summary>
        /// <para>
        /// Registers a block of code (callback) to run after ActorSystem.shutdown has been issued and all actors
        /// in this actor system have been stopped. Multiple code blocks may be registered by calling this method
        /// multiple times.
        /// </para>
        /// <para>
        /// The callbacks will be run sequentially in reverse order of registration, i.e. last registration is run first.
        /// </para>
        /// </summary>
        /// <param name="code">The code to run</param>
        /// <exception cref="Exception">
        /// This exception is thrown if the system has already shut down or if shutdown has been initiated.
        /// </exception>
        public abstract void RegisterOnTermination(Action code);

        /// <summary>
        /// <para>
        /// Terminates this actor system. This will stop the guardian actor, which in turn will recursively stop
        /// all its child actors, then the system guardian (below which the logging actors reside) and the execute
        /// all registered termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </para>
        /// <para>
        /// Be careful to not schedule any operations on completion of the returned task using the `dispatcher`
        /// of this actor system as it will have been shut down before the task completes.
        /// </para>
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> that will complete once the actor system has finished terminating and all actors are stopped.
        /// </returns>
        public abstract Task Terminate();

        /// <summary>
        /// Returns a task which will be completed after the <see cref="ActorSystem"/> has been
        /// terminated and termination hooks have been executed. Be careful to not schedule any
        /// operations on the `dispatcher` of this actor system as it will have been shut down
        /// before this task completes.
        /// </summary>
        public abstract Task WhenTerminated { get; }

        /// <summary>
        /// Stops the specified actor permanently.
        /// </summary>
        /// <param name="actor">The actor to stop</param>
        /// <remarks>
        /// This method has no effect if the actor is already stopped.
        /// </remarks>
        public abstract void Stop(IActorRef actor);

        private bool _isDisposed; //Automatically initialized to false;

        //Destructor:
        //~ActorSystem() 
        //{
        //    // Finalizer calls Dispose(false)
        //    Dispose(false);
        //}

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            //Take this object off the finalization queue and prevent finalization code for this object
            //from executing a second time.
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            // If disposing equals false, the method has been called by the
            // runtime from inside the finalizer and you should not reference
            // other objects. Only unmanaged resources can be disposed.

            try
            {
                //Make sure Dispose does not get called more than once, by checking the disposed field
                if (!_isDisposed)
                {
                    if (disposing)
                    {
                        Log.Debug("Disposing system");
                        Terminate();
                    }
                    //Clean up unmanaged resources
                }
                _isDisposed = true;
            }
            finally
            {
                
            }
        }

        /// <summary>
        /// Registers the specified extension with this actor system.
        /// </summary>
        /// <param name="extension">The extension to register with this actor system</param>
        /// <returns>The extension registered with this actor system</returns>
        public abstract object RegisterExtension(IExtensionId extension);

        /// <inheritdoc cref="IActorRefFactory"/>
        public abstract IActorRef ActorOf(Props props, string name = null);

        /// <inheritdoc cref="IActorRefFactory"/>
        public abstract ActorSelection ActorSelection(ActorPath actorPath);

        /// <inheritdoc cref="IActorRefFactory"/>
        public abstract ActorSelection ActorSelection(string actorPath);
    }
}

