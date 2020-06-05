using System;
using Akka.Configuration;
using Akka.Util;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class AeronSettings
    {
        public bool LogAeronCounters { get; }
        public bool EmbeddedMediaDriver { get; }
        public string AeronDirectoryName { get; }
        public bool DeleteAeronDirectory { get; }
        public int IdleCpuLevel { get; }
        public TimeSpan GiveUpMessageAfter { get; }
        public TimeSpan ClientLivenessTimeout { get; }
        public TimeSpan PublicationUnblockTimeout { get; }
        public TimeSpan ImageLivenessTimeout { get; }
        public TimeSpan DriverTimeout { get; }

        public AeronSettings(Config config)
        {
            var aeronConfig = config.GetConfig("aeron");
            if (aeronConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<AeronSettings>("akka.remote.artery.advanced.aeron");

            LogAeronCounters = aeronConfig.GetBoolean("log-aeron-counters");
            EmbeddedMediaDriver = aeronConfig.GetBoolean("embedded-media-driver");
            AeronDirectoryName = aeronConfig.GetString("aeron-dir")
                .Requiring(dir => EmbeddedMediaDriver || !string.IsNullOrEmpty(dir), "aeron-dir must be defined when using external media driver");
            DeleteAeronDirectory = aeronConfig.GetBoolean("delete-aeron-dir");
            IdleCpuLevel = aeronConfig.GetInt("idle-cpu-level")
                .Requiring(level => 1 <= level && level <= 10, "idle-cpu-level must be between 1 and 10");
            GiveUpMessageAfter = aeronConfig.GetTimeSpan("give-up-message-after")
                .Requiring(t => t > TimeSpan.Zero, "give-up-message-after must be more than zero");
            ClientLivenessTimeout = aeronConfig.GetTimeSpan("client-liveness-timeout")
                .Requiring(t => t > TimeSpan.Zero, "client-liveness-timeout must be more than zero");
            PublicationUnblockTimeout = aeronConfig.GetTimeSpan("publication-unblock-timeout")
                .Requiring(t => t > TimeSpan.Zero, "publication-unblock-timeout must be more than zero");
            ImageLivenessTimeout = aeronConfig.GetTimeSpan("image-liveness-timeout")
                .Requiring(t => t > TimeSpan.Zero, "image-liveness-timeout must be more than zero");
            DriverTimeout = aeronConfig.GetTimeSpan("driver-timeout")
                .Requiring(t => t > TimeSpan.Zero, "driver-timeout must be more than zero");
        }
    }
}
