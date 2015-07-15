//-----------------------------------------------------------------------
// <copyright file="Dns.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.IO
{
    public abstract class DnsBase
    {
        public virtual Dns.Resolved Cached(string name)
        {
            return null;
        }
        public virtual Dns.Resolved Resolve(string name, ActorSystem system, IActorRef sender)
        {
            var ret = Cached(name);
            if (ret == null)
                Dns.Instance.Apply(system).Manager.Tell(new Dns.Resolve(name), sender);
            return ret;
        }
    }

    public class Dns : ExtensionIdProvider<DnsExt>
    {
        public static readonly Dns Instance = new Dns();

        public abstract class Command
        { }

        public class Resolve : Command, IConsistentHashable
        {
            public Resolve(string name)
            {
                Name = name;
                ConsistentHashKey = name;
            }

            public object ConsistentHashKey { get; private set; }
            public string Name { get; private set; }
        }

        public class Resolved : Command
        {
            private readonly IPAddress _addr;

            public Resolved(string name, IEnumerable<IPAddress> ipv4, IEnumerable<IPAddress> ipv6)
            {
                Name = name;
                Ipv4 = ipv4;
                Ipv6 = ipv6;

                _addr = ipv4.FirstOrDefault() ?? ipv6.FirstOrDefault();
            }

            public string Name { get; private set; }
            public IEnumerable<IPAddress> Ipv4 { get; private set; }
            public IEnumerable<IPAddress> Ipv6 { get; private set; }

            public IPAddress Addr
            {
                get
                {
                    //TODO: Throw better exception
                    if (_addr == null) throw new Exception("Unknown host");
                    return _addr;
                }
            }

            public static Resolved Create(string name, IEnumerable<IPAddress> addresses)
            {
                var ipv4 = addresses.Where(x => x.AddressFamily == AddressFamily.InterNetwork);
                var ipv6 = addresses.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6);
                return new Resolved(name, ipv4, ipv6);
            }
        }

        public static Resolved Cached(string name, ActorSystem system)
        {
            return Instance.Apply(system).Cache.Cached(name);
        }

        public static Resolved ResolveName(string name, ActorSystem system, IActorRef sender)
        {
            return Instance.Apply(system).Cache.Resolve(name, system, sender);
        }

        public override DnsExt CreateExtension(ExtendedActorSystem system)
        {
            return new DnsExt(system);
        }
    }

    public class DnsExt : IOExtension
    {
        public class DnsSettings
        {
            public DnsSettings(Config config)
            {
                Dispatcher = config.GetString("dispatcher");
                Resolver = config.GetString("resolver");
                ResolverConfig = config.GetConfig(Resolver);
                ProviderObjectName = ResolverConfig.GetString("provider-object");
            }

            public string Dispatcher { get; private set; }
            public string Resolver { get; private set; }
            public Config ResolverConfig { get; private set; }
            public string ProviderObjectName { get; private set; }
        }
        
        private readonly ExtendedActorSystem _system;
        private IActorRef _manager;

        public DnsExt(ExtendedActorSystem system)
        {
            _system = system;
            Settings = new DnsSettings(system.Settings.Config.GetConfig("akka.io.dns"));
            //TODO: system.dynamicAccess.getClassFor[DnsProvider](Settings.ProviderObjectName).get.newInstance()
            Provider = (IDnsProvider) Activator.CreateInstance(Type.GetType(Settings.ProviderObjectName));
            Cache = Provider.Cache;
        }

        public override IActorRef Manager
        {
            get
            {
                return _manager = _manager ?? _system.SystemActorOf(Props.Create(() => new SimpleDnsManager(this))
                                                                         .WithDeploy(Deploy.Local)
                                                                         .WithDispatcher(Settings.Dispatcher));
            }
        }

        public IActorRef GetResolver()
        {
            return _manager;
        }

        public DnsSettings Settings { get; private set; }
        public DnsBase Cache { get; private set; }
        public IDnsProvider Provider { get; private set; }
    }
}
