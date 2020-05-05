//-----------------------------------------------------------------------
// <copyright file="RemoteMetricsExtension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Event;
using Akka.Routing;

namespace Akka.Remote
{
    /// <summary>
    ///     INTERNAL API
    ///     Extension that keeps track of remote metrics, such
    ///     as max size of different message types.
    /// </summary>
    internal class RemoteMetricsExtension : ExtensionIdProvider<IRemoteMetrics>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override IRemoteMetrics CreateExtension(ExtendedActorSystem system)
        {
            // TODO: Need to assert that config key exists. 
            var useLogFrameSize = 
                system.Settings.Config.GetString("akka.remote.log-frame-size-exceeding", string.Empty)
                .ToLowerInvariant();
            if (useLogFrameSize.Equals("off") ||
                useLogFrameSize.Equals("false") ||
                useLogFrameSize.Equals("no"))
            {
                return new RemoteMetricsOff();
            }
            return new RemoteMetricsOn(system);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static IRemoteMetrics Create(ExtendedActorSystem system)
        {
            return system.WithExtension<IRemoteMetrics, RemoteMetricsExtension>();
        }
    }

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    internal class RemoteMetricsOn : IRemoteMetrics
    {
        private readonly ILoggingAdapter _log;
        private readonly long? _logFrameSizeExceeding;
        private readonly ConcurrentDictionary<Type, long> _maxPayloadBytes = new ConcurrentDictionary<Type, long>();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public RemoteMetricsOn(ExtendedActorSystem system)
        {
            // TODO: Need to assert that config key exists
            _logFrameSizeExceeding = system.Settings.Config.GetByteSize("akka.remote.log-frame-size-exceeding", null);
            _log = Logging.GetLogger(system, this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="msg">TBD</param>
        /// <param name="payloadBytes">TBD</param>
        public void LogPayloadBytes(object msg, long payloadBytes)
        {
            if (payloadBytes >= _logFrameSizeExceeding)
            {
                Type type;
                if (msg is ActorSelectionMessage message)
                {
                    type = message.Message.GetType();
                }
                else if (msg is RouterEnvelope envelope)
                {
                    type = envelope.Message.GetType();
                }
                else
                {
                    type = msg.GetType();
                }

                // 10% threshold until next log
                var newMax = Convert.ToInt64(payloadBytes*1.1);
                Check(type, payloadBytes, newMax);
            }
        }
        private void Check(Type type, long payloadBytes, long newMax)
        {
            if (_maxPayloadBytes.TryGetValue(type, out long max))
            {
                if (payloadBytes > max)
                {
                    if (_maxPayloadBytes.TryUpdate(type, newMax, max))
                        _log.Info("New maximum payload size for [{0}] is [{1}] bytes", type.FullName, payloadBytes);
                    else
                        Check(type, payloadBytes, newMax);
                }
            }
            else
            {
                if (_maxPayloadBytes.TryAdd(type, newMax))
                    _log.Info("Payload size for [{0}] is [{1}] bytes", type.FullName, payloadBytes);
                else
                    Check(type, payloadBytes, newMax);
            }
        }
    }

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    internal class RemoteMetricsOff : IRemoteMetrics
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="msg">TBD</param>
        /// <param name="payloadBytes">TBD</param>
        public void LogPayloadBytes(object msg, long payloadBytes)
        {
            //do nothing
        }
    }

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    internal interface IRemoteMetrics : IExtension
    {
        /// <summary>
        ///     Logging of the size of different message types.
        ///     Maximum detected size per message type is logged once, with
        ///     and increase threshold of 10%.
        /// </summary>
        /// <param name="msg">TBD</param>
        /// <param name="payloadBytes">TBD</param>
        void LogPayloadBytes(object msg, long payloadBytes);
    }
}
