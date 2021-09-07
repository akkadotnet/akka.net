//-----------------------------------------------------------------------
// <copyright file="InetAddressDnsResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class InetAddressDnsResolver : ActorBase
    {
        private readonly SimpleDnsCache _cache;
        private readonly long _positiveTtl;
        private readonly long _negativeTtl;
        private readonly bool _useIpv6;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cache">TBD</param>
        /// <param name="config">TBD</param>
        public InetAddressDnsResolver(SimpleDnsCache cache, Config config)
        {
            _cache = cache;
            _positiveTtl = (long) config.GetTimeSpan("positive-ttl", null).TotalMilliseconds;
            _negativeTtl = (long) config.GetTimeSpan("negative-ttl", null).TotalMilliseconds;
            _useIpv6 = config.GetBoolean( "use-ipv6" , false);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if (message is Dns.Resolve resolve)
            {
                var replyTo = Sender;
                var answer = _cache.Cached(resolve.Name);
                if (answer != null)
                {
                    replyTo.Tell(answer);
                    return true;
                }
                
                System.Net.Dns.GetHostEntryAsync(resolve.Name).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        var flattened = t.Exception.Flatten().InnerExceptions;
                        return flattened.Count == 1 
                            ? new Dns.Resolved(resolve.Name, flattened[0]) 
                            : new Dns.Resolved(resolve.Name, t.Exception);
                    }
                    
                    answer = Dns.Resolved.Create(resolve.Name, t.Result.AddressList.Where(x => 
                        x.AddressFamily == AddressFamily.InterNetwork 
                        || _useIpv6 && x.AddressFamily == AddressFamily.InterNetworkV6));
                    _cache.Put(answer, _positiveTtl);
                    return answer;

                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(replyTo);
                return true;
            }
            return false;
        }
    }
}
