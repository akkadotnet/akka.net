//-----------------------------------------------------------------------
// <copyright file="ArteryRemoting.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="RemoteTransport"/> implementation for Artery TCP remoting (EXPERIMENTAL,
    /// under active development -- see <c>openspec/changes/artery-tcp-remoting/design.md</c>).
    ///
    /// <para>
    /// This is the gate-G2 "Configuration and entry point" skeleton (design.md, "Handshake +
    /// association/UID (gate G2)" -> "Provider integration", and Decision 1): it establishes
    /// this system's Artery <c>akka://</c> address and satisfies the <see cref="RemoteTransport"/>
    /// abstract surface so <see cref="RemoteActorRefProvider"/> can select it via config, but it
    /// does NOT yet implement the TCP pipeline, handshake, or association state -- those land in
    /// later Artery task groups (framing/envelope codec already exist under this namespace;
    /// handshake/association/TCP wiring are tracked separately).
    /// </para>
    ///
    /// <para>
    /// Artery uses the <c>akka://</c> scheme (NOT <c>akka.tcp://</c>, which is classic
    /// remoting's scheme) -- the two transports are not wire-compatible and a cluster must run
    /// one or the other homogeneously (design.md, "Provider integration").
    /// </para>
    /// </summary>
    internal sealed class ArteryRemoting : RemoteTransport
    {
        private readonly ArterySettings _settings;
        private readonly ILoggingAdapter _log;

        private volatile HashSet<Address>? _addresses;
        private volatile Address? _defaultAddress;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryRemoting"/> class.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="provider">TBD</param>
        public ArteryRemoting(ExtendedActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
            _log = Logging.GetLogger(system, "artery");
            _settings = new ArterySettings(system.Settings.Config.GetConfig("akka.remote.artery"));
        }

        /// <inheritdoc/>
        public override ISet<Address> Addresses => _addresses!;

        /// <inheritdoc/>
        public override Address DefaultAddress => _defaultAddress!;

        /// <inheritdoc/>
        public override void Start()
        {
            _log.Info("Starting Artery TCP remoting on [{0}:{1}]", _settings.CanonicalHostname, _settings.CanonicalPort);
            _log.Warning(
                "Artery TCP remoting is EXPERIMENTAL and under active development -- the transport, " +
                "handshake, and association layers are not yet implemented (gate G2 configuration/entry-point " +
                "skeleton only). Do not use in production.");

            var address = new Address("akka", System.Name, _settings.CanonicalHostname, _settings.CanonicalPort);
            _defaultAddress = address;
            _addresses = new HashSet<Address> { address };
        }

        /// <inheritdoc/>
        public override Task Shutdown()
        {
            _log.Info("Artery TCP remoting shut down");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override void Send(object message, IActorRef sender, RemoteActorRef recipient)
        {
            throw new NotImplementedException(
                "ArteryRemoting.Send is not yet implemented -- the Artery TCP outbound pipeline lands in a " +
                "later Artery milestone (gate G2 transport chunk, following the configuration/entry-point skeleton).");
        }

        /// <inheritdoc/>
        public override Task<bool> ManagementCommand(object cmd)
        {
            throw new NotImplementedException(
                "ArteryRemoting.ManagementCommand is not yet implemented -- it lands alongside the Artery " +
                "TCP transport (gate G2 transport chunk).");
        }

        /// <inheritdoc/>
        public override Task<bool> ManagementCommand(object cmd, CancellationToken cancellationToken)
        {
            throw new NotImplementedException(
                "ArteryRemoting.ManagementCommand is not yet implemented -- it lands alongside the Artery " +
                "TCP transport (gate G2 transport chunk).");
        }

        /// <inheritdoc/>
        public override Address LocalAddressForRemote(Address remote)
        {
            throw new NotImplementedException(
                "ArteryRemoting.LocalAddressForRemote is not yet implemented -- it lands alongside the Artery " +
                "TCP transport (gate G2 transport chunk).");
        }

        /// <inheritdoc/>
        public override void Quarantine(Address address, long? uid)
        {
            throw new NotImplementedException(
                "ArteryRemoting.Quarantine is not yet implemented -- UID-scoped quarantine lands with the " +
                "Artery association/handshake work (gate G2/G3, per design.md \"Handshake + association/UID\").");
        }
    }
}
