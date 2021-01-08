//-----------------------------------------------------------------------
// <copyright file="MsgDecoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Util;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit
{
    internal class MsgDecoder : MessageToMessageDecoder<object>
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<MsgDecoder>();

        public static Address Proto2Address(Serialization.Proto.Msg.AddressData addr)
        {
            return new Address(addr.Protocol, addr.System, addr.Hostname, (int)addr.Port);
        }

        public static ThrottleTransportAdapter.Direction Proto2Direction(Proto.Msg.InjectFailure.Types.Direction dir)
        {
            switch (dir)
            {
                case Proto.Msg.InjectFailure.Types.Direction.Send: return ThrottleTransportAdapter.Direction.Send;
                case Proto.Msg.InjectFailure.Types.Direction.Receive: return ThrottleTransportAdapter.Direction.Receive;
                case Proto.Msg.InjectFailure.Types.Direction.Both:
                default: return ThrottleTransportAdapter.Direction.Both;
            }
        }

        protected object Decode(object message)
        {
            _logger.LogDebug("Decoding {0}", message);

            var w = message as Proto.Msg.Wrapper;
            if (w != null)
            {
                if (w.Hello != null)
                {
                    return new Hello(w.Hello.Name, Proto2Address(w.Hello.Address));
                }
                else if (w.Barrier != null)
                {
                    switch (w.Barrier.Op)
                    {
                        case Proto.Msg.EnterBarrier.Types.BarrierOp.Succeeded: return new BarrierResult(w.Barrier.Name, true);
                        case Proto.Msg.EnterBarrier.Types.BarrierOp.Failed: return new BarrierResult(w.Barrier.Name, false);
                        case Proto.Msg.EnterBarrier.Types.BarrierOp.Fail: return new FailBarrier(w.Barrier.Name);
                        case Proto.Msg.EnterBarrier.Types.BarrierOp.Enter:
                            return new EnterBarrier(w.Barrier.Name, w.Barrier.Timeout > 0 ? (TimeSpan?)TimeSpan.FromTicks(w.Barrier.Timeout) : null);
                    }
                }
                else if (w.Failure != null)
                {
                    var f = w.Failure;
                    switch (f.Failure)
                    {
                        case Proto.Msg.InjectFailure.Types.FailType.Throttle:
                            return new ThrottleMsg(Proto2Address(f.Address), Proto2Direction(f.Direction), f.RateMBit);
                        case Proto.Msg.InjectFailure.Types.FailType.Abort:
                            return new DisconnectMsg(Proto2Address(f.Address), true);
                        case Proto.Msg.InjectFailure.Types.FailType.Disconnect:
                            return new DisconnectMsg(Proto2Address(f.Address), false);
                        case Proto.Msg.InjectFailure.Types.FailType.Exit:
                            return new TerminateMsg(new Right<bool, int>(f.ExitValue));
                        case Proto.Msg.InjectFailure.Types.FailType.Shutdown:
                            return new TerminateMsg(new Left<bool, int>(false));
                        case Proto.Msg.InjectFailure.Types.FailType.ShutdownAbrupt:
                            return new TerminateMsg(new Left<bool, int>(true));
                    }
                }
                else if (w.Addr != null)
                {
                    var a = w.Addr;
                    if (a.Addr != null)
                        return new AddressReply(new RoleName(a.Node), Proto2Address(a.Addr));

                    return new GetAddress(new RoleName(a.Node));
                }
                else if (!string.IsNullOrEmpty(w.Done))
                {
                    return Done.Instance;
                }
                else
                {
                    throw new ArgumentException($"unknown message {message}");
                }
            }

            throw new ArgumentException($"wrong message {message}");
        }

        protected override void Decode(IChannelHandlerContext context, object message, List<object> output)
        {
            var o = Decode(message);
            _logger.LogDebug("Decoded {0}", o);
            output.Add(o);
        }
    }
}
