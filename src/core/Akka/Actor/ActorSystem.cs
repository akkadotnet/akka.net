//-----------------------------------------------------------------------
// <copyright file="ActorSystem.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;

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
        ///     Creates a new ActorSystem with the specified name, and the specified Config
        /// </summary>
        /// <param name="name">Name of the ActorSystem
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <param name="config">Configuration of the ActorSystem</param>
        /// <returns>ActorSystem.</returns>
        public static ActorSystem Create(string name, Config config)
        {
            // var withFallback = config.WithFallback(ConfigurationFactory.Default());
            return CreateAndStartSystem(name, config);
        }

        /// <summary>
        ///     Creates the specified name.
        /// </summary>
        /// <param name="name">The name. The name must be uri friendly.
        /// <remarks>Must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-'</remarks>
        /// </param>
        /// <returns>ActorSystem.</returns>
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
        /// Returns an extension registered to this ActorSystem
        /// </summary>
        public abstract object GetExtension(IExtensionId extensionId);

        /// <summary>
        /// Returns an extension registered to this ActorSystem
        /// </summary>
        public abstract T GetExtension<T>() where T : class, IExtension;

        /// <summary>
        /// Determines whether this instance has the specified extension.
        /// </summary>
        public abstract bool HasExtension(Type t);

        /// <summary>
        /// Determines whether this instance has the specified extension.
        /// </summary>
        public abstract bool HasExtension<T>() where T : class, IExtension;

        /// <summary>
        /// Tries to the get the extension of specified type.
        /// </summary>
        public abstract bool TryGetExtension(Type extensionType, out object extension);

        /// <summary>
        /// Tries to the get the extension of specified type.
        /// </summary>
        public abstract bool TryGetExtension<T>(out T extension) where T : class, IExtension;

        /// <summary>
        /// Register a block of code (callback) to run after ActorSystem.shutdown has been issued and
        /// all actors in this actor system have been stopped.
        /// Multiple code blocks may be registered by calling this method multiple times.
        /// The callbacks will be run sequentially in reverse order of registration, i.e.
        /// last registration is run first.
        /// </summary>
        /// <param name="code">The code to run</param>
        /// <exception cref="Exception">Thrown if the System has already shut down or if shutdown has been initiated.</exception>
        public abstract void RegisterOnTermination(Action code);

        /// <summary>
        ///     Stop this actor system. This will stop the guardian actor, which in turn
        ///     will recursively stop all its child actors, then the system guardian
        ///     (below which the logging actors reside) and the execute all registered
        ///     termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </summary>
        public abstract void Shutdown();

        /// <summary>
        /// Returns a task that will be completed when the system has terminated.
        /// </summary>
        public abstract Task TerminationTask { get; }

        /// <summary>
        /// Block current thread until the system has been shutdown.
        /// This will block until after all on termination callbacks have been run.
        /// </summary>
        public abstract void AwaitTermination();

        /// <summary>
        /// Block current thread until the system has been shutdown, or the specified
        /// timeout has elapsed. 
        /// This will block until after all on termination callbacks have been run.
        /// <para>Returns <c>true</c> if the system was shutdown during the specified time;
        /// <c>false</c> if it timed out.</para>
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <returns>Returns <c>true</c> if the system was shutdown during the specified time;
        /// <c>false</c> if it timed out.</returns>
        public abstract bool AwaitTermination(TimeSpan timeout);

        /// <summary>
        /// Block current thread until the system has been shutdown, or the specified
        /// timeout has elapsed, or the cancellationToken was canceled. 
        /// This will block until after all on termination callbacks have been run.
        /// <para>Returns <c>true</c> if the system was shutdown during the specified time;
        /// <c>false</c> if it timed out, or the cancellationToken was canceled. </para>
        /// </summary>
        /// <param name="timeout">The timeout.</param>
        /// <param name="cancellationToken">A cancellation token that cancels the wait operation.</param>
        /// <returns>Returns <c>true</c> if the system was shutdown during the specified time;
        /// <c>false</c> if it timed out, or the cancellationToken was canceled. </returns>
        public abstract bool AwaitTermination(TimeSpan timeout, CancellationToken cancellationToken);


        public abstract void Stop(IActorRef actor);
        private bool _isDisposed; //Automatically initialized to false;

        //Destructor:
        //~ActorSystem() 
        //{
        //    // Finalizer calls Dispose(false)
        //    Dispose(false);
        //}

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            Dispose(true);
            //Take this object off the finalization queue and prevent finalization code for this object
            //from executing a second time.
            GC.SuppressFinalize(this);
        }


        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        /// <param name="disposing">if set to <c>true</c> the method has been called directly or indirectly by a 
        /// user's code. Managed and unmanaged resources will be disposed.<br />
        /// if set to <c>false</c> the method has been called by the runtime from inside the finalizer and only 
        /// unmanaged resources can be disposed.</param>
        private void Dispose(bool disposing)
        {
            // If disposing equals false, the method has been called by the
            // runtime from inside the finalizer and you should not reference
            // other objects. Only unmanaged resources can be disposed.

            try
            {
                //Make sure Dispose does not get called more than once, by checking the disposed field
                if(!_isDisposed)
                {
                    if(disposing)
                    {
                        Log.Debug("Disposing system");
                        Shutdown();
                    }
                    //Clean up unmanaged resources
                }
                _isDisposed = true;
            }
            finally
            {
                // base.dispose(disposing);
            }
        }


        public abstract object RegisterExtension(IExtensionId extension);

        public abstract IActorRef ActorOf(Props props, string name = null);

        public abstract ActorSelection ActorSelection(ActorPath actorPath);
        public abstract ActorSelection ActorSelection(string actorPath);

        /// <summary>
        /// Block and prevent the main application thread from exiting unless
        /// the actor system is shut down.
        /// </summary>
        [Obsolete("Use AwaitTermination instead")]
        public void WaitForShutdown()
        {
            AwaitTermination();
        }
    }
}

