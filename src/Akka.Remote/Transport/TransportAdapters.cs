using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Remote.Transport
{
    public interface ITransportAdapterProvider
    {
        /// <summary>
        /// Create a transport adapter that wraps the underlying transport
        /// </summary>
        Transport Create(Transport wrappedTransport, ActorSystem system);
    }

    public class TransportAdapters
    {
        public TransportAdapters(ActorSystem system)
        {
            System = system;
            Settings = ((RemoteActorRefProvider) system.Provider).RemoteSettings;
        }

        public ActorSystem System { get; private set; }

        protected RemoteSettings Settings;

        private Dictionary<string, ITransportAdapterProvider> _adaptersTable;

        private Dictionary<string, ITransportAdapterProvider> AdaptersTable()
        {
            if (_adaptersTable != null) return _adaptersTable;
            _adaptersTable = new Dictionary<string, ITransportAdapterProvider>();
            foreach (var adapter in Settings.Adapters)
            {
                try
                {
                    var adapterTypeName = Type.GetType(adapter.Value);
// ReSharper disable once AssignNullToNotNullAttribute
                    var newAdapter = (ITransportAdapterProvider)Activator.CreateInstance(adapterTypeName);
                    _adaptersTable.Add(adapter.Key, newAdapter);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(string.Format("Cannot initiate transport adapter {0}", adapter.Value), ex);
                }
            }

            return _adaptersTable;
        }

        public ITransportAdapterProvider GetAdapterProvider(string name)
        {
            if (_adaptersTable.ContainsKey(name))
            {
                return _adaptersTable[name];
            }

            throw new ArgumentException(string.Format("There is no registered transport adapter provider with name {0}", name));
        }
    }
}
