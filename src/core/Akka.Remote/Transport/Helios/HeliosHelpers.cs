//-----------------------------------------------------------------------
// <copyright file="HeliosHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Google.ProtocolBuffers;
using Helios;
using Helios.Buffers;
using Helios.Channels;
using Helios.Exceptions;
using Helios.Net;
using Helios.Ops;
using Helios.Serialization;
using Helios.Topology;

namespace Akka.Remote.Transport.Helios
{
    internal abstract class CommonHandlers : ChannelHandlerAdapter
    {
        protected readonly HeliosTransport WrappedTransport;
        protected readonly ILoggingAdapter Log;

        protected CommonHandlers(HeliosTransport wrappedTransport, ILoggingAdapter log)
        {
            WrappedTransport = wrappedTransport;
            Log = log;
        }

        public override void ChannelActive(IChannelHandlerContext context)
        {
            if (!WrappedTransport.ConnectionGroup.TryAdd(context.Channel))
            {
                Log.Warning("Unable to REMOVE channel [{0}->{1}](Id={2}) to connection group. May not shut down cleanly.", 
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        public override void ChannelInactive(IChannelHandlerContext context)
        {
            if (!WrappedTransport.ConnectionGroup.TryRemove(context.Channel))
            {
                Log.Warning("Unable to ADD channel [{0}->{1}](Id={2}) to connection group. May not shut down cleanly.",
                    context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Log.Error(exception, "Error caught channel [{0}->{1}](Id={2})", context.Channel.LocalAddress, context.Channel.RemoteAddress, context.Channel.Id);
        }

        protected abstract AssociationHandle CreateHandle(IChannel channel, Address localAddress,
            Address remoteAddress);

        protected abstract void RegisterListener(IChannel channel, IHandleEventListener listener, object msg,
            IPEndPoint remoteAddress);

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

