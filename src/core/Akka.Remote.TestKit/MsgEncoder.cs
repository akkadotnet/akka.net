﻿//-----------------------------------------------------------------------
// <copyright file="MsgEncoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Remote.TestKit.Proto;
using Akka.Remote.Transport;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Microsoft.Extensions.Logging;
using TCP;
using Address = Akka.Actor.Address;

namespace Akka.Remote.TestKit
{
    internal class MsgEncoder : MessageToMessageEncoder<object>
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<MsgEncoder>();

        public static TCP.Address Address2Proto(Address addr)
        {
            return TCP.Address.CreateBuilder()
                .SetProtocol(addr.Protocol)
                .SetSystem(addr.System)
                .SetHost(addr.Host)
                .SetPort(addr.Port.Value) //yep, it's FINE if this throws a null reference error - means that the test configuration is borked anyway
                .Build();
        }

        public static TCP.Direction Direction2Proto(ThrottleTransportAdapter.Direction dir)
        {
            switch (dir)
            {
                case ThrottleTransportAdapter.Direction.Send: return Direction.Send;
                case ThrottleTransportAdapter.Direction.Receive: return Direction.Receive;
                case ThrottleTransportAdapter.Direction.Both:
                default:
                    return Direction.Both;
            }
        }

        protected override void Encode(IChannelHandlerContext context, object message, List<object> output)
        {
            _logger.LogDebug("Encoding {0}", message);
            var w = Wrapper.CreateBuilder();
            message.Match()
                .With<Hello>(
                    hello =>
                        w.SetHello(
                            TCP.Hello.CreateBuilder()
                                .SetName(hello.Name)
                                .SetAddress(Address2Proto(hello.Address))))
                .With<EnterBarrier>(barrier =>
                {
                    var protoBarrier = TCP.EnterBarrier.CreateBuilder().SetName(barrier.Name);
                    if (barrier.Timeout.HasValue)
                        protoBarrier.SetTimeout(barrier.Timeout.Value.Ticks);
                    protoBarrier.SetOp(BarrierOp.Enter);
                    w.SetBarrier(protoBarrier);
                })
                .With<BarrierResult>(result =>
                {
                    var res = result.Success ? BarrierOp.Succeeded : BarrierOp.Failed;
                    w.SetBarrier(
                        TCP.EnterBarrier.CreateBuilder().SetName(result.Name).SetOp(res));
                })
                .With<FailBarrier>(
                    barrier =>
                        w.SetBarrier(TCP.EnterBarrier.CreateBuilder()
                            .SetName(barrier.Name)
                            .SetOp(BarrierOp.Fail)))
                .With<ThrottleMsg>(
                    throttle =>
                    {
                        w.SetFailure(
                            InjectFailure.CreateBuilder()
                                .SetFailure(TCP.FailType.Throttle)
                                .SetAddress(Address2Proto(throttle.Target))
                                .SetFailure(TCP.FailType.Throttle)
                                .SetDirection(Direction2Proto(throttle.Direction))
                                .SetRateMBit(throttle.RateMBit));
                    })
                .With<DisconnectMsg>(
                    disconnect =>
                        w.SetFailure(
                            InjectFailure.CreateBuilder()
                                .SetAddress(Address2Proto(disconnect.Target))
                                .SetFailure(disconnect.Abort ? TCP.FailType.Abort : TCP.FailType.Disconnect)))
                .With<TerminateMsg>(terminate =>
                {
                    if (terminate.ShutdownOrExit.IsRight)
                    {
                        w.SetFailure(
                            InjectFailure.CreateBuilder()
                                .SetFailure(TCP.FailType.Exit)
                                .SetExitValue(terminate.ShutdownOrExit.ToRight().Value));
                    }
                    else if (terminate.ShutdownOrExit.IsLeft && !terminate.ShutdownOrExit.ToLeft().Value)
                    {
                        w.SetFailure(
                            InjectFailure.CreateBuilder()
                                .SetFailure(TCP.FailType.Shutdown));
                    }
                    else
                    {
                        w.SetFailure(
                            InjectFailure.CreateBuilder()
                                .SetFailure(TCP.FailType.ShutdownAbrupt));
                    }

                })
                .With<GetAddress>(
                    address => w.SetAddr(AddressRequest.CreateBuilder().SetNode(address.Node.Name)))
                .With<AddressReply>(
                    reply =>
                        w.SetAddr(
                            AddressRequest.CreateBuilder()
                                .SetNode(reply.Node.Name)
                                .SetAddr(Address2Proto(reply.Addr))))
                .With<Done>(done => w.SetDone(string.Empty))
                .Default(obj => w.SetDone(string.Empty));

            output.Add(w.Build());
        }
    }
}

