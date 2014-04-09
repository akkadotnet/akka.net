using System;
using System.Threading.Tasks;
using Akka.Actor;
using Helios.Net;
using Helios.Topology;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Abstract base class for adapting Helios <see cref="IConnection"/> objects to work with Akka.Remote.Transports
    /// </summary>
    internal abstract class HeliosHelpers
    {
        protected abstract void OnConnect(INode remoteAddress, IConnection responseChannel);
        protected abstract void OnDisconnect(INode remoteAddress);

        protected abstract void OnOpen(IConnection openedChannel);

        protected abstract void OnMessage(NetworkData data, IConnection responseChannel);
    }

    internal abstract class CommonHandlers : HeliosHelpers
    {
        protected HeliosTransport Transport;

        protected override void OnOpen(IConnection openedChannel)
        {
            Transport.ConnectionGroup.TryAdd(openedChannel);
        }

        protected abstract AssociationHandle CreateHandle(IConnection channel, Address localAddress,
            Address remoteAddress);

        protected abstract void RegisterListener(IConnection channel, IHandleEventListener listener, NetworkData msg,
            INode remoteAddress);

        protected void Init(IConnection channel, INode remoteSocketAddress, Address remoteAddress, NetworkData msg,
            out AssociationHandle op)
        {
            var localAddress = HeliosTransport.NodeToAddress(channel.Local, Transport.SchemeIdentifier,
                Transport.System.Name, Transport.Settings.Hostname);

            if (localAddress != null)
            {
                var handle = CreateHandle(channel, localAddress, remoteAddress);
                handle.ReadHandlerSource.Task.ContinueWith(s =>
                {
                    var listener = s.Result;
                    RegisterListener(channel, listener, msg, remoteSocketAddress);
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
                op = handle;
            }
            else
            {
                op = null;
                channel.Close();
            }
        }
    }
}
