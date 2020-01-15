//-----------------------------------------------------------------------
// <copyright file="MsgEncoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Remote.Transport;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit
{
    internal class MsgEncoder : MessageToMessageEncoder<object>
    {
        private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<MsgEncoder>();

        private static Serialization.Proto.Msg.AddressData AddressMessageBuilder(Address address)
        {
            var message = new Serialization.Proto.Msg.AddressData();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        public static Proto.Msg.InjectFailure.Types.Direction Direction2Proto(ThrottleTransportAdapter.Direction dir)
        {
            switch (dir)
            {
                case ThrottleTransportAdapter.Direction.Send: return Proto.Msg.InjectFailure.Types.Direction.Send;
                case ThrottleTransportAdapter.Direction.Receive: return Proto.Msg.InjectFailure.Types.Direction.Receive;
                case ThrottleTransportAdapter.Direction.Both:
                default: return Proto.Msg.InjectFailure.Types.Direction.Both;
            }
        }

        protected override void Encode(IChannelHandlerContext context, object message, List<object> output)
        {
            _logger.LogDebug("Encoding {0}", message);

            var wrapper = new Proto.Msg.Wrapper();

            if (message is Hello)
            {
                var hello = (Hello)message;
                wrapper.Hello = new Proto.Msg.Hello
                {
                    Name = hello.Name,
                    Address = AddressMessageBuilder(hello.Address)
                };
            }
            else if (message is EnterBarrier)
            {
                var enterBarrier = (EnterBarrier)message;
                wrapper.Barrier = new Proto.Msg.EnterBarrier
                {
                    Name = enterBarrier.Name,
                    Timeout = enterBarrier.Timeout?.Ticks ?? 0,
                    Op = Proto.Msg.EnterBarrier.Types.BarrierOp.Enter,
                };
            }
            else if (message is BarrierResult)
            {
                var barrierResult = (BarrierResult)message;
                wrapper.Barrier = new Proto.Msg.EnterBarrier
                {
                    Name = barrierResult.Name,
                    Op = barrierResult.Success
                        ? Proto.Msg.EnterBarrier.Types.BarrierOp.Succeeded
                        : Proto.Msg.EnterBarrier.Types.BarrierOp.Failed
                };
            }
            else if (message is FailBarrier)
            {
                var failBarrier = (FailBarrier)message;
                wrapper.Barrier = new Proto.Msg.EnterBarrier
                {
                    Name = failBarrier.Name,
                    Op = Proto.Msg.EnterBarrier.Types.BarrierOp.Fail
                };
            }
            else if (message is ThrottleMsg)
            {
                var throttleMsg = (ThrottleMsg)message;
                wrapper.Failure = new Proto.Msg.InjectFailure
                {
                    Address = AddressMessageBuilder(throttleMsg.Target),
                    Failure = Proto.Msg.InjectFailure.Types.FailType.Throttle,
                    Direction = Direction2Proto(throttleMsg.Direction),
                    RateMBit = throttleMsg.RateMBit
                };
            }
            else if (message is DisconnectMsg)
            {
                var disconnectMsg = (DisconnectMsg)message;
                wrapper.Failure = new Proto.Msg.InjectFailure
                {
                    Address = AddressMessageBuilder(disconnectMsg.Target),
                    Failure = disconnectMsg.Abort
                        ? Proto.Msg.InjectFailure.Types.FailType.Abort
                        : Proto.Msg.InjectFailure.Types.FailType.Disconnect
                };
            }
            else if (message is TerminateMsg)
            {
                var terminate = (TerminateMsg)message;
                if (terminate.ShutdownOrExit.IsRight)
                {
                    wrapper.Failure = new Proto.Msg.InjectFailure()
                    {
                        Failure = Proto.Msg.InjectFailure.Types.FailType.Exit,
                        ExitValue = terminate.ShutdownOrExit.ToRight().Value
                    };
                }
                else if (terminate.ShutdownOrExit.IsLeft && !terminate.ShutdownOrExit.ToLeft().Value)
                {
                    wrapper.Failure = new Proto.Msg.InjectFailure()
                    {
                        Failure = Proto.Msg.InjectFailure.Types.FailType.Shutdown
                    };
                }
                else
                {
                    wrapper.Failure = new Proto.Msg.InjectFailure()
                    {
                        Failure = Proto.Msg.InjectFailure.Types.FailType.ShutdownAbrupt
                    };
                }
            }
            else if (message is GetAddress)
            {
                var getAddress = (GetAddress)message;
                wrapper.Addr = new Proto.Msg.AddressRequest { Node = getAddress.Node.Name };
            }
            else if (message is AddressReply)
            {
                var addressReply = (AddressReply)message;
                wrapper.Addr = new Proto.Msg.AddressRequest
                {
                    Node = addressReply.Node.Name,
                    Addr = AddressMessageBuilder(addressReply.Addr)
                };
            }
            else if (message is Done)
            {
                wrapper.Done = " ";
            }

            output.Add(wrapper);
        }
    }
}
