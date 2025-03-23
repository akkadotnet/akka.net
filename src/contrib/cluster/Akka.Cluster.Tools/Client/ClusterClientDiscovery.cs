//-----------------------------------------------------------------------
// <copyright file="ClusterClientDiscovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Discovery;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using Newtonsoft.Json;

#nullable enable
namespace Akka.Cluster.Tools.Client;

public class ClusterClientDiscovery: UntypedActor, IWithUnboundedStash, IWithTimers
{
    #region Discovery messages

    internal sealed class DiscoverTick: IEquatable<DiscoverTick>
    {
        public static readonly DiscoverTick Instance = new();

        private DiscoverTick() { }
        public bool Equals(DiscoverTick? other) => other is not null;
        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || obj is DiscoverTick;
        public override int GetHashCode() => 0;
    }

    private sealed record DiscoveryFailure(Exception Cause);
    private sealed record ContactVerificationFailure(Exception Cause);
    private sealed record ReceptionistHttpPayload(string ReceptionistPath);
    
    #endregion
    
    private const string DiscoveryTimerKey = "resolve-key";
    
    private readonly TimeSpan _defaultReconnectTimeout = TimeSpan.FromSeconds(10);
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly ClusterClientSettings _settings;

    private readonly ServiceDiscovery? _serviceDiscovery;
    private readonly HttpClient _http; 
    private readonly Lookup _lookup;
    private readonly TimeSpan _discoveryTimeout;
    private readonly TimeSpan _discoveryRetryInterval;
    private readonly double _exponentialBackoffJitter;
    private readonly TimeSpan _exponentialBackoffMax;
    private readonly TimeSpan _probeTimeout;
    private readonly int _numberOfContacts;
    private readonly CancellationTokenSource _shutdownCts = new ();
    private int _discoveryFailedBackoffCounter;

    private readonly bool _verboseLogging;
    
    public ClusterClientDiscovery(ClusterClientSettings settings)
    {
        _settings = settings;
        var discoverySettings = settings.DiscoverySettings;
        
        if(_settings.InitialContacts.Count > 0)
            _log.Warning("Initial contacts is being ignored because ClusterClient contacts discovery is being used");
        
        var discoveryMethod = discoverySettings.DiscoveryMethod;
        if(string.IsNullOrWhiteSpace(discoveryMethod) || discoveryMethod == "<method>")
        {
            _log.Info(
                "No default initial contacts discovery implementation configured in\n" +
                "`akka.cluster.client.discovery.method`. Trying to Fall back to default\n" +
                "discovery method declared in `akka.discovery.method`");
            discoveryMethod = Context.System.Settings.Config.GetString("akka.discovery.method");
            
            if (string.IsNullOrWhiteSpace(discoveryMethod) || discoveryMethod == "<method>")
            {
                _log.Warning(
                    "No default initial contacts discovery implementation configured in both\n" +
                    "`akka.cluster.client.discovery.method` and `akka.discovery.method`.\n" +
                    "Make sure to configure this setting to your preferred implementation such as 'config'\n" +
                    "in your application.conf (from the akka-discovery module). Falling back to default config\n" +
                    "based discovery method");
                discoveryMethod = "config";
            }
        }

        if (_settings.ReconnectTimeout is null)
        {
            _log.Warning(
                "No reconnect timeout were configured in `akka.cluster.client.reconnect-timeout`,\n" +
                "this setting is required when using cluster client initial contact discovery feature.\n" +
                "Falling back to default value ({0}) instead.", _defaultReconnectTimeout);
            _settings = _settings.WithReconnectTimeout(_defaultReconnectTimeout);
        }

        _http = new HttpClient();
        _http.Timeout = _settings.DiscoverySettings.ProbeTimeout;
        
        _lookup = new Lookup(discoverySettings.ServiceName, discoverySettings.PortName);
        _serviceDiscovery = Discovery.Discovery.Get(Context.System).LoadServiceDiscovery(discoveryMethod);
        _discoveryRetryInterval = _settings.DiscoverySettings.Interval;
        _discoveryTimeout = discoverySettings.ResolveTimeout;
        _exponentialBackoffJitter = discoverySettings.ExponentialBackoffJitter;
        _exponentialBackoffMax = discoverySettings.ExponentialBackoffMax;
        _probeTimeout = discoverySettings.ProbeTimeout;

        _numberOfContacts = discoverySettings.NumberOfContacts;
        
        _verboseLogging = _settings.VerboseLogging;
        
        Become(Discovering);
    }

    public IStash Stash { get; set; } = null!;
    public ITimerScheduler Timers { get; set; } = null!;
    
    protected override void OnReceive(object message)
    {
        throw new NotImplementedException("Should never reach this code");
    }

    protected override void PreStart()
    {
        base.PreStart();

        var shutdown = CoordinatedShutdown.Get(Context.System);
        shutdown.AddTask(
            CoordinatedShutdown.PhaseBeforeServiceUnbind,
            "stop-cluster-client-discovery",
            () =>
            {
                _shutdownCts.Cancel();
                return Task.FromResult(Done.Instance);
            });
        
        // Kickoff discovery lookup
        Self.Tell(DiscoverTick.Instance);
    }

    protected override void PostStop()
    {
        base.PostStop();
        _shutdownCts.Dispose();
        _http.Dispose();
        Timers.CancelAll();
    }

    private async Task<ActorPath?> ResolveContact(
        ServiceDiscovery.ResolvedTarget resolved,
        IActorContext context,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (resolved.Address is null && string.IsNullOrWhiteSpace(resolved.Host))
        {
            if(_verboseLogging && _log.IsWarningEnabled)
                _log.Warning($"Akka.Discovery resolved an entry with empty address and host: {resolved}");
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        
        var address = resolved.Address?.ToString() ?? resolved.Host;
        var port = resolved.Port.HasValue ? $":{resolved.Port}" : string.Empty;
        var uri = new Uri($"http://{address}{port}/cluster-client/receptionist");
        
        try
        {
            string json;
            using var response = await _http.GetAsync(uri, cts.Token);
            {
                if (!response.IsSuccessStatusCode)
                {
                    if(_verboseLogging && _log.IsInfoEnabled)
                        _log.Info($"Failed to probe {uri}, Akka.Management HTTP endpoint returns {response.StatusCode}");
                    return null;
                }
                
                json = await response.Content.ReadAsStringAsync(ct);
                
                if(string.IsNullOrWhiteSpace(json))
                {
                    if(_verboseLogging && _log.IsWarningEnabled)
                        _log.Warning($"Failed to probe {uri}, Akka.Management HTTP endpoint returns empty body");
                    return null;
                }
            }
            
            var payload = JsonConvert.DeserializeObject<ReceptionistHttpPayload>(json);
            if(payload is null || string.IsNullOrWhiteSpace(payload.ReceptionistPath))
            {
                if(_verboseLogging && _log.IsWarningEnabled)
                    _log.Info($"Failed to probe {uri}, Could not deserialize HTTP JSON body. Body: {json}");
                return null;
            }
            
            var actorPath = ActorPath.Parse(payload.ReceptionistPath);
            var selection = context.ActorSelection(actorPath);
            
            var identity = await selection.Ask<ActorIdentity>(new Identify(null), cts.Token);
            return identity.Subject is null ? null : actorPath;
        }
        catch (Exception ex)
        {
            if(_verboseLogging && _log.IsWarningEnabled)
                _log.Warning(ex, $"Failed to probe {uri}");
            return null;
        }
    }
    
    private void StartSingleDiscoveryTimer()
    {
        TimeSpan interval;
        try
        {
            var rnd = 1.0 + ThreadLocalRandom.Current.NextDouble() * _exponentialBackoffJitter;
            var ticks = _discoveryRetryInterval.Ticks * Math.Pow(2, _discoveryFailedBackoffCounter);
            ticks = Math.Min(_exponentialBackoffMax.Ticks, ticks) * rnd;
            interval = new TimeSpan((long) ticks);
        }
        catch
        {
            interval = _exponentialBackoffMax;
        }
        
        Timers.StartSingleTimer(DiscoveryTimerKey, DiscoverTick.Instance, interval);
    }
    
    private bool Discovering(object message)
    {
        switch (message)
        {
            case DiscoverTick:
                if(_verboseLogging && _log.IsDebugEnabled)
                    _log.Debug("Discovering initial contacts");
        
                _serviceDiscovery!.Lookup(_lookup, _discoveryTimeout)
                    .PipeTo(Self, Self, failure: cause => new DiscoveryFailure(cause));
                return true;

            case ServiceDiscovery.Resolved resolved:
            {
                Timers.CancelAll();
                
                if (resolved.Addresses.Count == 0)
                {
                    if(_verboseLogging && _log.IsInfoEnabled)
                        _log.Info("No initial contact were discovered. Will try again.");
        
                    // discovery didn't find any contacts, retry discovery
                    _discoveryFailedBackoffCounter++;
                    StartSingleDiscoveryTimer();
                    return true;
                }

                if (!resolved.ServiceName.Equals(_lookup.ServiceName))
                {
                    if(_log.IsWarningEnabled)
                        _log.Warning($"Received Akka.Discovery result for wrong service name. Expected: {_lookup.ServiceName}, got: {resolved.ServiceName}");
                    
                    // discovery returned wrong service, retry discovery
                    _discoveryFailedBackoffCounter++;
                    StartSingleDiscoveryTimer();
                    return true;
                }

                if(_verboseLogging && _log.IsDebugEnabled)
                    _log.Debug("Initial contacts are discovered at [{0}], verifying existence.", string.Join(", ", resolved.Addresses));
        
                VerifyContacts().PipeTo(Self, Self, failure: cause => new ContactVerificationFailure(cause));

                return true;

                async Task<ActorPath?[]> VerifyContacts()
                {
                    var tasks = resolved.Addresses.Select(c => ResolveContact(c, Context, _probeTimeout, _shutdownCts.Token));
                    return await Task.WhenAll(tasks);
                }
            }

            case ActorPath?[] resolved:
            {
#pragma warning disable CS8619 // Converting null literal or possible null value to non-nullable type.
                var contacts = (ActorPath[]) resolved.Where(r => r is not null).ToArray();
#pragma warning restore CS8619 // Converting null literal or possible null value to non-nullable type.
                if (contacts.Length == 0)
                {
                    if(_verboseLogging && _log.IsInfoEnabled)
                        _log.Info("Cluster client contact point resolution phase failed, will try again.");

                    _discoveryFailedBackoffCounter++;
                    StartSingleDiscoveryTimer();
                }
                else
                {
                    var filteredContacts = TrimContacts(contacts, _numberOfContacts);
                    if(_log.IsInfoEnabled)
                        _log.Info(
                            "Cluster client initial contacts are verified at [{0}], starting cluster client actor.", 
                            string.Join(", ", filteredContacts.Select(p => p.ToString())));
                    
                    Become(Active(filteredContacts));
                }
                
                return true;
            }
            
            case DiscoveryFailure fail:
                if(_verboseLogging && _log.IsInfoEnabled)
                    _log.Info(fail.Cause, "Cluster client contact point service discovery phase failed, will try again.");

                _discoveryFailedBackoffCounter++;
                StartSingleDiscoveryTimer();
                return true;
            
            case ContactVerificationFailure fail:
                if(_verboseLogging && _log.IsInfoEnabled)
                    _log.Info(fail.Cause, "Cluster client receptionist path verification phase failed, will try again.");

                _discoveryFailedBackoffCounter++;
                StartSingleDiscoveryTimer();
                return true;
            
            default:
                Stash.Stash();
                return true;
        }
    }

    /// <summary>
    /// Trim the number of Contact in the `fullContact` array to `count` length
    /// by picking random elements while avoiding repeating elements from being returned
    /// </summary>
    /// <param name="fullContact">Array of Contacts</param>
    /// <param name="count">The number of elements to return</param>
    /// <returns></returns>
    private static ActorPath[] TrimContacts(ActorPath[] fullContact, int count)
    {
        if (fullContact.Length <= count)
            return fullContact;
        
        fullContact.Shuffle();
        return fullContact.Take(count).ToArray();
    }

    private Receive Active(ActorPath[] contacts)
    {
        if(_verboseLogging && _log.IsDebugEnabled)
            _log.Debug("Entering active state");
        
        _discoveryFailedBackoffCounter = 0;
        Timers.CancelAll();
        
        // Setup cluster client initial contacts
        var currentSettings = _settings.WithInitialContacts(contacts.ToImmutableHashSet());
        
        var clusterClient = Context.System.ActorOf(Props.Create(() => new ClusterClient(currentSettings)).WithDeploy(Deploy.Local));
        Context.Watch(clusterClient);
        Stash.UnstashAll();

        return message =>
        {
            switch (message)
            {
                case Terminated terminated:
                    if (terminated.ActorRef.Equals(clusterClient))
                    {
                        if(_verboseLogging && _log.IsInfoEnabled)
                            _log.Info("Cluster client failed to reconnect to all receptionists, rediscovering.");
                        
                        // Kickoff discovery lookup
                        Self.Tell(DiscoverTick.Instance);
                        Become(Discovering);
                    }
                    else
                    {
                        clusterClient.Forward(message);
                    }
                    break;
                
                default:
                    clusterClient.Forward(message);
                    break;
            }

            return true;
        };
    }
}
