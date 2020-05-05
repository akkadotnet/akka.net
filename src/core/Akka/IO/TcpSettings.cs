//-----------------------------------------------------------------------
// <copyright file="TcpSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class TcpSettings
    {
        /// <summary>
        /// Creates a new instance of <see cref="TcpSettings"/> class 
        /// and fills it with values parsed from `akka.io.tcp` HOCON
        /// path found in actor system.
        /// </summary>
        public static TcpSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.io.tcp");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<TcpSettings>("akka.io.tcp");//($"Failed to create {typeof(TcpSettings)}: akka.io.tcp configuration node not found");

            return Create(config);
        }

        /// <summary>
        /// Creates a new instance of <see cref="TcpSettings"/> class 
        /// and fills it with values parsed from provided HOCON config.
        /// </summary>
        /// <param name="config">TBD</param>
        public static TcpSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<TcpSettings>();

            return new TcpSettings(
                bufferPoolConfigPath: config.GetString("buffer-pool", "akka.io.tcp.direct-buffer-pool"),
                initialSocketAsyncEventArgs: config.GetInt("nr-of-socket-async-event-args", 32),
                traceLogging: config.GetBoolean("trace-logging", false),
                batchAcceptLimit: config.GetInt("batch-accept-limit", 10),
                registerTimeout: config.GetTimeSpan("register-timeout", TimeSpan.FromSeconds(5)),
                receivedMessageSizeLimit: config.GetString("max-received-message-size", "unlimited") == "unlimited"
                    ? int.MaxValue
                    : config.GetInt("max-received-message-size", 0),
                managementDispatcher: config.GetString("management-dispatcher", "akka.actor.default-dispatcher"),
                fileIoDispatcher: config.GetString("file-io-dispatcher", "akka.actor.default-dispatcher"),
                transferToLimit: config.GetString("file-io-transferTo-limit", null) == "unlimited"
                    ? int.MaxValue
                    : config.GetInt("file-io-transferTo-limit", 512 * 1024),
                finishConnectRetries: config.GetInt("finish-connect-retries", 5),
                outgoingSocketForceIpv4: config.GetBoolean("outgoing-socket-force-ipv4", false),
                writeCommandsQueueMaxSize: config.GetInt("write-commands-queue-max-size", -1));
        }

        public TcpSettings( string    bufferPoolConfigPath,
                            int       initialSocketAsyncEventArgs,
                            bool      traceLogging,
                            int       batchAcceptLimit,
                            TimeSpan? registerTimeout,
                            int       receivedMessageSizeLimit,
                            string    managementDispatcher,
                            string    fileIoDispatcher,
                            int       transferToLimit,
                            int       finishConnectRetries,
                            bool      outgoingSocketForceIpv4,
                            int       writeCommandsQueueMaxSize)
        {
            BufferPoolConfigPath = bufferPoolConfigPath;
            InitialSocketAsyncEventArgs = initialSocketAsyncEventArgs;
            TraceLogging = traceLogging;
            BatchAcceptLimit = batchAcceptLimit;
            RegisterTimeout = registerTimeout;
            ReceivedMessageSizeLimit = receivedMessageSizeLimit;
            ManagementDispatcher = managementDispatcher;
            FileIODispatcher = fileIoDispatcher;
            TransferToLimit = transferToLimit;
            FinishConnectRetries = finishConnectRetries;
            OutgoingSocketForceIpv4 = outgoingSocketForceIpv4;
            WriteCommandsQueueMaxSize = writeCommandsQueueMaxSize;
        }

        /// <summary>
        /// A config path to the section defining which byte buffer pool to use.
        /// Buffer pools are used to mitigate GC-pressure made by potentiall allocation
        /// and deallocation of byte buffers used for writing/receiving data from sockets.
        /// </summary>
        public string BufferPoolConfigPath { get; }

        /// <summary>
        /// The initial number of SocketAsyncEventArgs to be preallocated. This value
        /// will grow infinitely if needed.
        /// </summary>
        public int InitialSocketAsyncEventArgs { get; }

        /// <summary>
        /// Enable fine grained logging of what goes on inside the implementation. 
        /// Be aware that this may log more than once per message sent to the 
        /// actors of the tcp implementation.
        /// </summary>
        public bool TraceLogging { get; }

        /// <summary>
        /// The maximum number of connection that are accepted in one go, higher 
        /// numbers decrease latency, lower numbers increase fairness on the 
        /// worker-dispatcher
        /// </summary>
        public int BatchAcceptLimit { get; }
        
        /// <summary>
        /// The duration a connection actor waits for a `Register` message from 
        /// its commander before aborting the connection.
        /// </summary>
        public TimeSpan? RegisterTimeout { get; }

        /// <summary>
        /// The maximum number of bytes delivered by a `Received` message. Before
        /// more data is read from the network the connection actor will try to
        /// do other work.
        /// The purpose of this setting is to impose a smaller limit than the 
        /// configured receive buffer size. When using value 'unlimited' it will
        /// try to read all from the receive buffer.
        /// </summary>
        public int ReceivedMessageSizeLimit { get; }

        /// <summary>
        /// Fully qualified config path which holds the dispatcher configuration
        /// for the selector management actors
        /// </summary>
        public string ManagementDispatcher { get; }

        /// <summary>
        /// Fully qualified config path which holds the dispatcher configuration
        /// on which file IO tasks are scheduled
        /// </summary>
        public string FileIODispatcher { get; }

        /// <summary>
        /// The maximum number of bytes (or "unlimited") to transfer in one batch
        /// when using `WriteFile` command which uses `FileChannel.transferTo` to
        /// pipe files to a TCP socket. On some OS like Linux `FileChannel.transferTo`
        /// may block for a long time when network IO is faster than file IO.
        /// Decreasing the value may improve fairness while increasing may improve
        /// throughput.
        /// </summary>
        public int TransferToLimit { get; set; }

        /// <summary>
        /// The number of times to retry the `finishConnect` call after being notified about
        /// OP_CONNECT. Retries are needed if the OP_CONNECT notification doesn't imply that
        /// `finishConnect` will succeed, which is the case on Android.
        /// </summary>
        public int FinishConnectRetries { get; }

        /// <summary>
        /// Enforce outgoing socket connection to use IPv4 address family. Required in
        /// scenario when IPv6 is not available, for example in Azure Web App sandbox.
        /// When set to true it is required to set akka.io.dns.inet-address.use-ipv6 to false
        /// in cases when DnsEndPoint is used to describe the remote address
        /// </summary>
        public bool OutgoingSocketForceIpv4 { get; }
        
        /// <summary>
        /// Limits maximum size of internal queue, used in <see cref="TcpIncomingConnection"/> connection actor
        /// to store pending write commands.
        /// To allow unlimited size, set to -1.
        /// </summary>
        public int WriteCommandsQueueMaxSize { get; }
    }
}
