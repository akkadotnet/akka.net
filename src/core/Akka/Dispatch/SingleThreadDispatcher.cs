//-----------------------------------------------------------------------
// <copyright file="SingleThreadDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Helios.Concurrency;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used to create instances of the <see cref="SingleThreadDispatcher"/>. 
    /// 
    /// Each actor created using the pinned dispatcher gets its own unique thread.
    /// <remarks>
    /// Always returns a new instance.
    /// </remarks>
    /// </summary>
    class PinnedDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly DedicatedThreadPoolSettings _settings;

        public PinnedDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            var dtp = config.GetConfig("dedicated-thread-pool");
            if (dtp == null || dtp.IsEmpty)
            {
                _settings = DedicatedThreadPoolConfigHelpers.DefaultSingleThreadPoolSettings;
            }
            else
            {
                _settings = new DedicatedThreadPoolSettings(1,
                    DedicatedThreadPoolConfigHelpers.ConfigureThreadType(dtp.GetString("threadtype",
                        ThreadType.Background.ToString())),
                    config.GetString("id"),
                    DedicatedThreadPoolConfigHelpers.GetSafeDeadlockTimeout(dtp),
                    DedicatedThreadPoolConfigHelpers.GetApartmentState(dtp));
            }
        }

        public override MessageDispatcher Dispatcher()
        {
            return new SingleThreadDispatcher(this, _settings);
        }
    }


    /// <summary>
    /// Used to power the <see cref="PinnedDispatcherConfigurator"/>.
    /// 
    /// Guaranteed to provide one new thread instance per actor.
    /// 
    /// Uses <see cref="DedicatedThreadPool"/> with 1 thread in order 
    /// to take advantage of standard cleanup / teardown / queueing mechanics.
    /// 
    /// /// Relevant configuration options:
    /// <code>
    ///     my-forkjoin-dispatcher{
    ///             type = PinnedDispatcher
    ///	            throughput = 100
    ///	            dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
    ///		            #deadlock-timeout = 3s #optional timeout for deadlock detection
    ///		            threadtype = background #values can be "background" or "foreground"
    ///                 apartment = mta # values can be "mta" or "sta" or empty
    ///	            }
    ///     }
    /// 
    ///     my-other-forkjoin-dispatcher{
    ///             type = PinnedDispatcher
    ///             # dedicated-thread-pool section is optional
    ///     }
    /// </code>
    /// <remarks>
    /// Worth noting that unlike the <see cref="ForkJoinDispatcher"/>, the <see cref="SingleThreadDispatcher"/>
    /// does not respect the <c>dedicated-thread-pool.thread-count</c> property in configuration. That value is
    /// always equal to 1 in the <see cref="SingleThreadDispatcher"/>.
    /// </remarks>
    /// </summary>
    public class SingleThreadDispatcher : MessageDispatcher
    {
        private readonly DedicatedThreadPool _dedicatedThreadPool;

        internal SingleThreadDispatcher(MessageDispatcherConfigurator configurator, DedicatedThreadPoolSettings settings)
            : base(configurator)
        {
            _dedicatedThreadPool = new DedicatedThreadPool(settings);
        }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public override void Schedule(Action run)
        {
            _dedicatedThreadPool.QueueUserWorkItem(run);
        }

        public override void Detach(ActorCell cell)
        {
            //shut down the dedicated thread pool
            _dedicatedThreadPool.Dispose();
        }
    }
}