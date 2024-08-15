// -----------------------------------------------------------------------
//  <copyright file="MsgEncoder.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.TestKit.Proto.Msg;
using Akka.Remote.Transport;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Microsoft.Extensions.Logging;

namespace Akka.Remote.TestKit;

internal class MsgEncoder : MessageToMessageEncoder<object>
{
    private readonly ILogger _logger = InternalLoggerFactory.DefaultFactory.CreateLogger<MsgEncoder>();

    private static AddressData AddressMessageBuilder(Address address)
    {
        var message = new AddressData();
        message.System = address.System;
        message.Hostname = address.Host;
        message.Port = (uint)(address.Port ?? 0);
        message.Protocol = address.Protocol;
        return message;
    }

    public static InjectFailure.Types.Direction Direction2Proto(ThrottleTransportAdapter.Direction dir)
    {
        switch (dir)
        {
            case ThrottleTransportAdapter.Direction.Send: return InjectFailure.Types.Direction.Send;
            case ThrottleTransportAdapter.Direction.Receive: return InjectFailure.Types.Direction.Receive;
            case ThrottleTransportAdapter.Direction.Both:
            default: return InjectFailure.Types.Direction.Both;
        }
    }

    protected override void Encode(IChannelHandlerContext context, object message, List<object> output)
    {
        _logger.LogDebug("Encoding {0}", message);

        var wrapper = new Wrapper();

        if (message is Hello hello)
        {
            wrapper.Hello = new Proto.Msg.Hello { Name = hello.Name, Address = AddressMessageBuilder(hello.Address) };
        }
        else if (message is EnterBarrier enterBarrier)
        {
            wrapper.Barrier = new Proto.Msg.EnterBarrier
            {
                Name = enterBarrier.Name,
                Timeout = enterBarrier.Timeout?.Ticks ?? 0,
                Op = Proto.Msg.EnterBarrier.Types.BarrierOp.Enter
            };
        }
        else if (message is BarrierResult barrierResult)
        {
            wrapper.Barrier = new Proto.Msg.EnterBarrier
            {
                Name = barrierResult.Name,
                Op = barrierResult.Success
                    ? Proto.Msg.EnterBarrier.Types.BarrierOp.Succeeded
                    : Proto.Msg.EnterBarrier.Types.BarrierOp.Failed
            };
        }
        else if (message is FailBarrier failBarrier)
        {
            wrapper.Barrier = new Proto.Msg.EnterBarrier
            {
                Name = failBarrier.Name, Op = Proto.Msg.EnterBarrier.Types.BarrierOp.Fail
            };
        }
        else if (message is ThrottleMsg throttleMsg)
        {
            wrapper.Failure = new InjectFailure
            {
                Address = AddressMessageBuilder(throttleMsg.Target),
                Failure = InjectFailure.Types.FailType.Throttle,
                Direction = Direction2Proto(throttleMsg.Direction),
                RateMBit = throttleMsg.RateMBit
            };
        }
        else if (message is DisconnectMsg disconnectMsg)
        {
            wrapper.Failure = new InjectFailure
            {
                Address = AddressMessageBuilder(disconnectMsg.Target),
                Failure = disconnectMsg.Abort
                    ? InjectFailure.Types.FailType.Abort
                    : InjectFailure.Types.FailType.Disconnect
            };
        }
        else if (message is TerminateMsg terminate)
        {
            if (terminate.ShutdownOrExit.IsRight)
                wrapper.Failure = new InjectFailure
                {
                    Failure = InjectFailure.Types.FailType.Exit,
                    ExitValue = terminate.ShutdownOrExit.ToRight().Value
                };
            else if (terminate.ShutdownOrExit.IsLeft && !terminate.ShutdownOrExit.ToLeft().Value)
                wrapper.Failure = new InjectFailure { Failure = InjectFailure.Types.FailType.Shutdown };
            else
                wrapper.Failure = new InjectFailure { Failure = InjectFailure.Types.FailType.ShutdownAbrupt };
        }
        else if (message is GetAddress getAddress)
        {
            wrapper.Addr = new AddressRequest { Node = getAddress.Node.Name };
        }
        else if (message is AddressReply addressReply)
        {
            wrapper.Addr = new AddressRequest
            {
                Node = addressReply.Node.Name, Addr = AddressMessageBuilder(addressReply.Addr)
            };
        }
        else if (message is Done)
        {
            wrapper.Done = " ";
        }

        output.Add(wrapper);
    }
}