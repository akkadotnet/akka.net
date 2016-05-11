//-----------------------------------------------------------------------
// <copyright file="SimpleDnsManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;

namespace Akka.IO
{
    public class SimpleDnsManager : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly DnsExt _ext;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _resolver;
        private IPeriodicCacheCleanup _cacheCleanup;
        private ICancelable _cleanupTimer;

        public SimpleDnsManager(DnsExt ext)
        {
            _ext = ext;
            _resolver = Context.ActorOf(Props.Create(ext.Provider.ActorClass, ext.Cache, ext.Settings.ResolverConfig)
                                             .WithDeploy(Deploy.Local)
                                             .WithDispatcher(ext.Settings.Dispatcher));

            _cacheCleanup = _ext.Cache as IPeriodicCacheCleanup;

            if (_cacheCleanup != null)
            {
                var interval = ext.Settings.ResolverConfig.GetTimeSpan("cache-cleanup-interval");
                _cleanupTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(interval, interval, Self, CacheCleanup.Instance, Self);
            }
        }

        protected override bool Receive(object message)
        {
            var r = message as Dns.Resolve;
            if (r != null)
            {
                _log.Debug("Resolution request for {0} from {1}", r.Name, Sender);
                _resolver.Forward(r);
            }
            if (message is CacheCleanup)
            {
                if (_cacheCleanup != null)
                    _cacheCleanup.CleanUp();
                return true;
            }
            return false;
        }

        protected override void PostStop()
        {
            if (_cleanupTimer != null)
                _cleanupTimer.Cancel();
        }

        internal class CacheCleanup
        {
            public static readonly CacheCleanup Instance = new CacheCleanup();
        }
    }
}
