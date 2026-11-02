using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;
using Akka.Discovery;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ClusterClientDiscoverySpecs
{
    [Fact(DisplayName = "Empty ClusterClientDiscoveryOptions should generate default values")]
    public void EmptyClusterClientDiscoverySpec()
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "test");
        var options = new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = "whatever"
        };
        builder.ApplyClusterClientDiscovery<Service>(options);
        
        var defaultConfig = ClusterClientReceptionist.DefaultConfig().GetConfig("akka.cluster.client");
        var systemConfig = builder.Configuration.Value;
        var config = ConfigurationFactory.ParseString(options.ToString())
            .WithFallback(systemConfig.GetConfig("akka.cluster.client"));

        Assert.True(config.GetBoolean("use-initial-contacts-discovery"));
        Assert.Equal(ConfigServiceDiscoveryOptions.DefaultPath, config.GetString("discovery.method"));
        Assert.Equal("whatever", config.GetString("discovery.service-name"));
        
        defaultConfig.AssertSameString(config, "discovery.port-name");
        defaultConfig.AssertSameInt(config, "discovery.number-of-contacts");
        defaultConfig.AssertSameTimeSpan(config, "discovery.interval");
        defaultConfig.AssertSameTimeSpan(config, "discovery.resolve-timeout");

        Assert.Equal("config", systemConfig.GetString("akka.discovery.method"));
    }
    
    [Fact(DisplayName = "ClusterClientDiscoverySettings should be set correctly")]
    public void ClusterClientDiscoverySettingsSpec()
    {
        var options = new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions
            {
                ConfigPath = "custom",
                IsDefaultPlugin = false,
                Services = new List<Service>
                {
                    new ()
                    {
                        Name = "testService",
                        Endpoints = new[] { "ep1", "ep2" }
                    }
                }
            }, 
            ServiceName = "testService", 
            PortName = "testPort", 
            Timeout = 1.Seconds(),
            RetryInterval = 2.Seconds(), 
            NumberOfContacts = 10
        };

        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "test");
        builder.ApplyClusterClientDiscovery<Service>(options);

        var systemConfig = builder.Configuration.Value;
        var config = ConfigurationFactory.ParseString(options.ToString())
            .WithFallback(systemConfig.GetConfig("akka.cluster.client"));
        var settings = ClusterClientSettings.Create(config);

        Assert.Equal("custom", config.GetString("discovery.method"));
        Assert.True(config.GetBoolean("use-initial-contacts-discovery"));
        Assert.Empty(settings.InitialContacts);
        Assert.Equal("testService", settings.DiscoverySettings.ServiceName);
        Assert.Equal("testPort", settings.DiscoverySettings.PortName);
        Assert.Equal(1.Seconds(), settings.DiscoverySettings.ResolveTimeout);
        Assert.Equal(2.Seconds(), settings.DiscoverySettings.Interval);
        Assert.Equal(10, settings.DiscoverySettings.NumberOfContacts);

        Assert.Equal("<method>", systemConfig.GetString("akka.discovery.method"));
        
        var discoveryConfig = systemConfig.GetConfig("akka.discovery.custom");
        Assert.NotNull(discoveryConfig);
        Assert.Equal("akka.discovery.custom.services", discoveryConfig.GetString("services-path"));
        Assert.NotNull(discoveryConfig.GetConfig("services"));
    }

    [Fact(DisplayName = "ClusterClientDiscoverySettings with invalid values should throw")]
    public void ClusterClientDiscoveryInvalidSettingsSpec()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = null!
        }.ToString());
        Assert.StartsWith("Service name must be provided", ex.Message);


        ex = Assert.Throws<ArgumentException>(() => new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = string.Empty
        }.ToString());
        Assert.StartsWith("Service name must be provided", ex.Message);

        ex = Assert.Throws<ArgumentException>(() => new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = "whatever",
            Timeout = Timeout.InfiniteTimeSpan
        }.ToString());
        Assert.StartsWith("Timeout must be greater than zero", ex.Message);

        ex = Assert.Throws<ArgumentException>(() => new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = "whatever",
            NumberOfContacts = 0
        }.ToString());
        Assert.StartsWith("Number of contacts must be greater than zero", ex.Message);
        
        ex = Assert.Throws<ArgumentException>(() => new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = "whatever",
            ClientActorName = string.Empty
        }.ToString());
        Assert.StartsWith("Cluster client actor name must not be empty or whitespace", ex.Message);

        ex = Assert.Throws<ArgumentException>(() => new ClusterClientDiscoveryOptions
        {
            DiscoveryOptions = new ConfigServiceDiscoveryOptions(),
            ServiceName = "whatever",
            ClientActorName = " "
        }.ToString());
        Assert.StartsWith("Cluster client actor name must not be empty or whitespace", ex.Message);
    }
    
    private class ConfigServiceDiscoveryOptions: IDiscoveryOptions
    {
        internal const string DefaultPath = "config";
        internal const string DefaultConfigPath = "akka.discovery." + DefaultPath;
        public static string FullPath(string path) => $"akka.discovery.{path}";
    
        public string ConfigPath { get; set; } = DefaultPath;
    
        public Type Class { get; } = typeof(ConfigServiceDiscovery);
    
        public List<Service> Services { get; set; } = new (); 
        public bool IsDefaultPlugin { get; set; } = true;

        public void Apply(AkkaConfigurationBuilder builder, Setup? inputSetup = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{FullPath(ConfigPath)} {{");
            sb.AppendLine($"class = {Class.AssemblyQualifiedName!.ToHocon()}");
            sb.AppendLine($"services-path = {FullPath(ConfigPath)}.services");

            sb.AppendLine("services {");
            foreach (var service in Services)
            {
                service.Apply(sb);
            }
            sb.AppendLine("}");
        
            sb.AppendLine("}");
        
            if(IsDefaultPlugin)
                sb.AppendLine($"akka.discovery.method = {ConfigPath}");

            builder.AddHocon(sb.ToString(), HoconAddMode.Prepend);
        
            var fallback = DiscoveryProvider.DefaultConfiguration()
                .GetConfig(DefaultConfigPath)
                .MoveTo(FullPath(ConfigPath));
            builder.AddHocon(fallback, HoconAddMode.Append);
        }
    }
    
    private class Service
    {
        public string Name { get; set; } = string.Empty;
        public string[] Endpoints { get; set; } = Array.Empty<string>();

        internal StringBuilder Apply(StringBuilder builder)
        {
            builder.AppendLine($"{Name} {{");
            builder.AppendLine($"endpoints = [ { string.Join(",", Endpoints.Select(s => s.ToHocon()))} ]");
            builder.AppendLine("}");

            return builder;
        }
    }
    
    public class ConfigServiceDiscovery : ServiceDiscovery
    {
        private const string DefaultPath = "config";
        private const string DefaultConfigPath = "akka.discovery." + DefaultPath;
        
        public ConfigServiceDiscovery(ExtendedActorSystem system, Config config)
        {
        }

        public override Task<Resolved> Lookup(Lookup lookup, TimeSpan resolveTimeout)
        {
            return Task.FromResult(new Resolved(lookup.ServiceName, null));
        }
    }
    
}