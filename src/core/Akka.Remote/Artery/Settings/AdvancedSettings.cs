using System;
using Akka.Configuration;
using Akka.Streams;
using Akka.Util;

namespace Akka.Remote.Artery.Settings
{
    internal sealed class AdvancedSettings
    {
        public bool TestMode { get; }
        public string Dispatcher { get; }
        public string ControlStreamDispatcher { get; }

        [Obsolete("deprecated")]
        public ActorMaterializerSettings MaterializerSettings { get; }
        [Obsolete("deprecated")]
        public ActorMaterializerSettings ControlStreamMaterializerSettings { get; }

        public int OutboundLanes { get; }
        public int InboundLanes { get; }
        public int SysMsgBufferSize { get; }
        public int OutboundMessageQueueSize { get; }
        public int OutboundControlQueueSize { get; }
        public int OutboundLargeMessageQueueSize { get; }
        public TimeSpan SystemMessageResendInterval { get; }
        public TimeSpan HandshakeTimeout { get; }
        public TimeSpan HandshakeRetryInterval { get; }
        public TimeSpan InjectHandshakeInterval { get; }
        public TimeSpan GiveUpSystemMessageAfter { get; }
        public TimeSpan StopIdleOutboundAfter { get; }
        public TimeSpan QuarantineIdleOutboundAfter { get; }
        public TimeSpan StopQuarantinedAfterIdle { get; }
        public TimeSpan RemoveQuarantinedAssociationAfter { get; }
        public TimeSpan ShutdownFlushTimeout { get; }
        public TimeSpan InboundRestartTimeout { get; }
        public int InboundMaxRestarts { get; }
        public TimeSpan OutboundRestartBackoff { get; }
        public TimeSpan OutboundRestartTimeout { get; }
        public int OutboundMaxRestarts { get; }
        public CompressionSettings Compression { get; }
        public int MaximumFrameSize { get; }
        public int BufferPoolSize { get; }
        public int InboundHubBufferSize { get; }
        public int MaximumLargeFrameSize { get; }
        public int LargeBufferPoolSize { get; }
        public AeronSettings Aeron { get; }
        public TcpSettings Tcp { get; }

        public AdvancedSettings(Config advancedConfig)
        {
            if (advancedConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<AdvancedSettings>("akka.remote.artery.advanced");

            TestMode = advancedConfig.GetBoolean("test-mode");
            Dispatcher = advancedConfig.GetString("use-dispatcher");
            ControlStreamDispatcher = advancedConfig.GetString("use-control-stream-dispatcher");

            MaterializerSettings =
                ActorMaterializerSettings.Create(advancedConfig.GetConfig("materializer"))
                .WithDispatcher(Dispatcher);
            ControlStreamMaterializerSettings =
                ActorMaterializerSettings.Create(advancedConfig.GetConfig("materializer"))
                .WithDispatcher(ControlStreamDispatcher);

            OutboundLanes = advancedConfig.GetInt("outbound-lanes")
                .Requiring(i => i > 0, "outbound-lanes must be greater than zero.");
            InboundLanes = advancedConfig.GetInt("inbound-lanes")
                .Requiring(i => i > 0, "inbound-lanes must be greater than zero.");
            SysMsgBufferSize = advancedConfig.GetInt("system-message-buffer-size")
                .Requiring(i => i > 0, "system-message-buffer-size must be greater than zero.");
            OutboundMessageQueueSize = advancedConfig.GetInt("outbound-message-queue-size")
                .Requiring(i => i > 0, "outbound-message-queue-size must be greater than zero.");
            OutboundControlQueueSize = advancedConfig.GetInt("outbound-control-queue-size")
                .Requiring(i => i > 0, "outbound-control-queue-size must be greater than zero.");
            OutboundLargeMessageQueueSize = advancedConfig.GetInt("outbound-large-message-queue-size")
                .Requiring(i => i > 0, "outbound-large-message-queue-size must be greater than zero.");
            SystemMessageResendInterval = advancedConfig.GetTimeSpan("system-message-resend-interval")
                .Requiring(t => t > TimeSpan.Zero, "system-message-resend-interval must be greater than zero.");
            HandshakeTimeout = advancedConfig.GetTimeSpan("handshake-timeout")
                .Requiring(t => t > TimeSpan.Zero, "handshake-timeout must be greater than zero.");
            HandshakeRetryInterval = advancedConfig.GetTimeSpan("handshake-retry-interval")
                .Requiring(t => t > TimeSpan.Zero, "handshake-retry-interval must be greater than zero.");
            InjectHandshakeInterval = advancedConfig.GetTimeSpan("inject-handshake-interval")
                .Requiring(t => t > TimeSpan.Zero, "inject-handshake-interval must be greater than zero.");
            GiveUpSystemMessageAfter = advancedConfig.GetTimeSpan("give-up-system-message-after")
                .Requiring(t => t > TimeSpan.Zero, "give-up-system-message-after must be greater than zero.");
            StopIdleOutboundAfter = advancedConfig.GetTimeSpan("stop-idle-outbound-after")
                .Requiring(t => t > TimeSpan.Zero, "stop-idle-outbound-after must be greater than zero.");
            QuarantineIdleOutboundAfter = advancedConfig.GetTimeSpan("quarantine-idle-outbound-after")
                .Requiring(t => t > TimeSpan.Zero, "quarantine-idle-outbound-after must be greater than zero.");
            StopQuarantinedAfterIdle = advancedConfig.GetTimeSpan("stop-quarantined-after-idle")
                .Requiring(t => t > TimeSpan.Zero, "stop-quarantined-after-idle must be greater than zero.");
            RemoveQuarantinedAssociationAfter = advancedConfig.GetTimeSpan("remove-quarantined-association-after")
                .Requiring(t => t > TimeSpan.Zero, "remove-quarantined-association-after must be greater than zero.");
            ShutdownFlushTimeout = advancedConfig.GetTimeSpan("shutdown-flush-timeout")
                .Requiring(t => t > TimeSpan.Zero, "shutdown-flush-timeout must be greater than zero.");
            InboundRestartTimeout = advancedConfig.GetTimeSpan("inbound-restart-timeout")
                .Requiring(t => t > TimeSpan.Zero, "inbound-restart-timeout must be greater than zero.");
            InboundMaxRestarts = advancedConfig.GetInt("inbound-max-restarts");
            OutboundRestartBackoff = advancedConfig.GetTimeSpan("outbound-restart-backoff")
                .Requiring(t => t > TimeSpan.Zero, "outbound-restart-backoff must be greater than zero.");
            OutboundRestartTimeout = advancedConfig.GetTimeSpan("outbound-restart-timeout")
                .Requiring(t => t > TimeSpan.Zero, "outbound-restart-timeout must be greater than zero.");
            OutboundMaxRestarts = advancedConfig.GetInt("outbound-max-restarts");

            Compression = new CompressionSettings(advancedConfig.GetConfig("compression"));

            MaximumFrameSize = Math.Min((int)advancedConfig.GetByteSize("maximum-frame-size").Value, int.MaxValue)
                .Requiring(i => i >= 32 * 1024, "maximum-frame-size must be greater than or equal to 32 KiB");
            BufferPoolSize = advancedConfig.GetInt("buffer-pool-size")
                .Requiring(i => i > 0, "buffer-pool-size must be greater than zero.");
            InboundHubBufferSize = BufferPoolSize / 2;
            MaximumLargeFrameSize = Math.Min((int)advancedConfig.GetByteSize("maximum-large-frame-size").Value, int.MaxValue)
                .Requiring(i => i >= 32 * 1024, "maximum-large-frame-size must be greater than or equal to 32 KiB");
            LargeBufferPoolSize = advancedConfig.GetInt("large-buffer-pool-size")
                .Requiring(i => i > 0, "large-buffer-pool-size must be greater than zero.");

            Aeron = new AeronSettings(advancedConfig.GetConfig("aeron"));
            Tcp = new TcpSettings(advancedConfig.GetConfig("tcp"));
        }
    }
}
