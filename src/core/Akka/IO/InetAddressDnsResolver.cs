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
                    Dns.Resolved newAnswer;
                    if (t.IsFaulted)
                    {
                        foreach (var exception in t.Exception.Flatten().InnerExceptions)
                        {
                            if (exception is SocketException se && se.SocketErrorCode == SocketError.HostNotFound)
                            {
                                newAnswer = new Dns.Resolved(resolve.Name, Enumerable.Empty<IPAddress>(), Enumerable.Empty<IPAddress>());
                                _cache.Put(newAnswer, _negativeTtl);
                                return newAnswer;
                            }
                        }
                        ExceptionDispatchInfo.Capture(t.Exception).Throw();
                    }
                    
                    newAnswer = Dns.Resolved.Create(resolve.Name, t.Result.AddressList.Where(x => 
                        x.AddressFamily == AddressFamily.InterNetwork 
                        || _useIpv6 && x.AddressFamily == AddressFamily.InterNetworkV6));
                    _cache.Put(newAnswer, _positiveTtl);
                    return newAnswer;

                }, TaskContinuationOptions.ExecuteSynchronously).PipeTo(replyTo);
                return true;
            }
            return false;
        }
    }
}
