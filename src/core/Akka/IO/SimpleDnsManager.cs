//-----------------------------------------------------------------------
// <copyright file="SimpleDnsManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// Actor that manages DNS resolution requests and the DNS cache.
    /// </summary>
    public class SimpleDnsManager : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly DnsExt _ext;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _resolver;
        private IPeriodicCacheCleanup _cacheCleanup;
        private ICancelable _cleanupTimer;

        /// <summary>
        /// Creates a new instance of the SimpleDnsManager.
        /// </summary>
        /// <param name="ext">The DNS extension that owns this manager.</param>
        public SimpleDnsManager(DnsExt ext)
        {
            _ext = ext;
            _resolver = Context.ActorOf(Props.Create(ext.Provider.ActorClass, ext.Cache, ext.Settings.ResolverConfig)
                                             .WithDeploy(Deploy.Local)
                                             .WithDispatcher(ext.Settings.Dispatcher));

            _cacheCleanup = _ext.Cache as IPeriodicCacheCleanup;

            if (_cacheCleanup != null)
            {
                var interval = ext.Settings.ResolverConfig.GetTimeSpan("cache-cleanup-interval", null);
                _cleanupTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, interval, Self, CacheCleanup.Instance, Self);
            }
        }

        /// <summary>
        /// Handles DNS resolution requests and cache cleanup messages.
        /// </summary>
        /// <param name="message">The message to process.</param>
        /// <returns>True if the message was handled, false otherwise.</returns>
        protected override bool Receive(object message)
        {
            var r = message as Dns.Resolve;
            if (r != null)
            {
                _log.Debug("Resolution request for {0} from {1}", r.Name, Sender);
                _resolver.Forward(r);
                return true;
            }
            if (message is CacheCleanup)
            {
                if (_cacheCleanup != null)
                    _cacheCleanup.CleanUp();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cancels the cleanup timer when the actor is stopped.
        /// </summary>
        protected override void PostStop()
        {
            if (_cleanupTimer != null)
                _cleanupTimer.Cancel();
        }

        /// <summary>
        /// Message sent to trigger DNS cache cleanup.
        /// </summary>
        internal class CacheCleanup
        {
            /// <summary>
            /// Singleton instance of the cache cleanup message.
            /// </summary>
            public static readonly CacheCleanup Instance = new();
        }
    }
}
