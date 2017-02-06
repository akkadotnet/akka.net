#region copyright
// -----------------------------------------------------------------------
//  <copyright file="HeliosHelpers.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Helios.Channels;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class CommonHandlers : ChannelHandlerAdapter
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly HeliosTransport WrappedTransport;
        /// <summary>
        /// TBD
        /// </summary>
        protected readonly ILoggingAdapter Log;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="log">TBD</param>
        protected CommonHandlers(HeliosTransport wrappedTransport, ILoggingAdapter log)
        {
            WrappedTransport = wrappedTransport;
            Log = log;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        public override void ChannelActive(IChannelHandlerContext context)
        {
            if (!WrappedTransport.ConnectionGroup.TryAdd(context.Channel))
            {
                Log.Warning("Unable to REMOVE channel [{0}->{1}](Id={2}) to connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        public override void ChannelInactive(IChannelHandlerContext context)
        {
            if (!WrappedTransport.ConnectionGroup.TryRemove(context.Channel))
            {
                Log.Warning("Unable to ADD channel [{0}->{1}](Id={2}) to connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="exception">TBD</param>
        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Log.Error(exception, "Error caught channel [{0}->{1}](Id={2})", context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="channel">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        protected abstract AssociationHandle CreateHandle(IChannel channel, Address localAddress,
            Address remoteAddress);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="channel">TBD</param>
        /// <param name="listener">TBD</param>
        /// <param name="msg">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        protected abstract void RegisterListener(IChannel channel, IHandleEventListener listener, object msg,
            IPEndPoint remoteAddress);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="channel">TBD</param>
        /// <param name="remoteSocketAddress">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="msg">TBD</param>
        /// <param name="op">TBD</param>
        protected void Init(IChannel channel, IPEndPoint remoteSocketAddress, Address remoteAddress, object msg,
            out AssociationHandle op)
        {
            var localAddress = HeliosTransport.MapSocketToAddress((IPEndPoint)channel.LocalAddress, WrappedTransport.SchemeIdentifier,
                WrappedTransport.System.Name, WrappedTransport.Settings.Hostname);

            if (localAddress != null)
            {
                var handle = CreateHandle(channel, localAddress, remoteAddress);
                handle.ReadHandlerSource.Task.ContinueWith(s =>
                {
                    var listener = s.Result;
                    RegisterListener(channel, listener, msg, remoteSocketAddress);
                    channel.Configuration.AutoRead = true; // turn reads back on
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.NotOnCanceled | TaskContinuationOptions.NotOnFaulted);
                op = handle;
            }
            else
            {
                op = null;
                channel.CloseAsync();
            }
        }
    }
}
