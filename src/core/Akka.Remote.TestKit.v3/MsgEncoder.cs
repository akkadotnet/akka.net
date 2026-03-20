//-----------------------------------------------------------------------
// <copyright file="MsgEncoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Remote.TestKit.Proto.Msg;
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

        public static TestKit.Proto.Msg.InjectFailure.Types.Direction Direction2Proto(ThrottleTransportAdapter.Direction dir)
        {
            switch (dir)
            {
                case ThrottleTransportAdapter.Direction.Send: return TestKit.Proto.Msg.InjectFailure.Types.Direction.Send;
                case ThrottleTransportAdapter.Direction.Receive: return TestKit.Proto.Msg.InjectFailure.Types.Direction.Receive;
                case ThrottleTransportAdapter.Direction.Both:
                default: return TestKit.Proto.Msg.InjectFailure.Types.Direction.Both;
            }
        }

        protected override void Encode(IChannelHandlerContext context, object message, List<object> output)
        {
            _logger.LogDebug("Encoding {0}", message);

            var wrapper = new TestKit.Proto.Msg.Wrapper();

            switch (message)
            {
                case Hello hello:
                    wrapper.Hello = new TestKit.Proto.Msg.Hello
                    {
                        Name = hello.Name,
                        Address = AddressMessageBuilder(hello.Address)
                    };
                    break;
                case EnterBarrier enterBarrier:
                    wrapper.Barrier = new TestKit.Proto.Msg.EnterBarrier
                    {
                        Name = enterBarrier.Name,
                        Timeout = enterBarrier.Timeout?.Ticks ?? 0,
                        Op = TestKit.Proto.Msg.EnterBarrier.Types.BarrierOp.Enter,
                        RoleName = enterBarrier.Role.Name
                    };
                    break;
                case BarrierResult barrierResult:
                    wrapper.Barrier = new TestKit.Proto.Msg.EnterBarrier
                    {
                        Name = barrierResult.Name,
                        Op = barrierResult.Success
                            ? TestKit.Proto.Msg.EnterBarrier.Types.BarrierOp.Succeeded
                            : TestKit.Proto.Msg.EnterBarrier.Types.BarrierOp.Failed
                    };
                    break;
                case FailBarrier failBarrier:
                    wrapper.Barrier = new TestKit.Proto.Msg.EnterBarrier
                    {
                        Name = failBarrier.Name,
                        Op = TestKit.Proto.Msg.EnterBarrier.Types.BarrierOp.Fail,
                        RoleName = failBarrier.Role.Name
                    };
                    break;
                case ThrottleMsg throttleMsg:
                    wrapper.Failure = new TestKit.Proto.Msg.InjectFailure
                    {
                        Address = AddressMessageBuilder(throttleMsg.Target),
                        Failure = TestKit.Proto.Msg.InjectFailure.Types.FailType.Throttle,
                        Direction = Direction2Proto(throttleMsg.Direction),
                        RateMBit = throttleMsg.RateMBit
                    };
                    break;
                case DisconnectMsg disconnectMsg:
                    wrapper.Failure = new TestKit.Proto.Msg.InjectFailure
                    {
                        Address = AddressMessageBuilder(disconnectMsg.Target),
                        Failure = disconnectMsg.Abort
                            ? TestKit.Proto.Msg.InjectFailure.Types.FailType.Abort
                            : TestKit.Proto.Msg.InjectFailure.Types.FailType.Disconnect
                    };
                    break;
                case TerminateMsg terminate when terminate.ShutdownOrExit.IsRight:
                    wrapper.Failure = new TestKit.Proto.Msg.InjectFailure()
                    {
                        Failure = TestKit.Proto.Msg.InjectFailure.Types.FailType.Exit,
                        ExitValue = terminate.ShutdownOrExit.ToRight().Value
                    };
                    break;
                case TerminateMsg terminate when terminate.ShutdownOrExit.IsLeft && !terminate.ShutdownOrExit.ToLeft().Value:
                    wrapper.Failure = new TestKit.Proto.Msg.InjectFailure()
                    {
                        Failure = TestKit.Proto.Msg.InjectFailure.Types.FailType.Shutdown
                    };
                    break;
                case TerminateMsg terminate:
                    wrapper.Failure = new TestKit.Proto.Msg.InjectFailure()
                    {
                        Failure = TestKit.Proto.Msg.InjectFailure.Types.FailType.ShutdownAbrupt
                    };
                    break;
                case GetAddress getAddress:
                    wrapper.Addr = new TestKit.Proto.Msg.AddressRequest { Node = getAddress.Node.Name };
                    break;
                case AddressReply addressReply:
                    wrapper.Addr = new TestKit.Proto.Msg.AddressRequest
                    {
                        Node = addressReply.Node.Name,
                        Addr = AddressMessageBuilder(addressReply.Addr)
                    };
                    break;
                case Done:
                    wrapper.Done = " ";
                    break;
            }

            output.Add(wrapper);
        }
    }
}
