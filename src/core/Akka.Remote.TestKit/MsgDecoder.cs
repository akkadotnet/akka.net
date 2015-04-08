//-----------------------------------------------------------------------
// <copyright file="MsgDecoder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Remote.Transport;
using Akka.Util;
using TCP;
using Address = Akka.Actor.Address;

namespace Akka.Remote.TestKit
{
    internal class MsgDecoder
    {
        public static Address Proto2Address(TCP.Address addr)
        {
            return new Address(addr.Protocol, addr.System, addr.Host, addr.Port);
        }

        public static ThrottleTransportAdapter.Direction Proto2Direction(TCP.Direction dir)
        {
            switch (dir)
            {
                case Direction.Send:
                    return ThrottleTransportAdapter.Direction.Send;
                case Direction.Receive:
                    return ThrottleTransportAdapter.Direction.Receive;
                case Direction.Both:
                default:
                    return ThrottleTransportAdapter.Direction.Both;
            }
        }

        public object Decode(object message)
        {
            var w = message as TCP.Wrapper;
            if (w != null && w.AllFields.Count == 1)
            {
                if (w.HasHello)
                {
                    var h = w.Hello;
                    return new Hello(h.Name, Proto2Address(h.Address));
                }
                else if (w.HasBarrier)
                {
                    var barrier = w.Barrier;
                    switch (barrier.Op)
                    {
                        case BarrierOp.Succeeded: return (new BarrierResult(barrier.Name, true));
                        case BarrierOp.Failed: return (new BarrierResult(barrier.Name, false));
                        case BarrierOp.Fail: return (new FailBarrier(barrier.Name));
                        case BarrierOp.Enter:
                            return (new EnterBarrier(barrier.Name, barrier.HasTimeout ? (TimeSpan?)TimeSpan.FromTicks(barrier.Timeout) : null));
                    }
                }
                else if (w.HasFailure)
                {
                    var f = w.Failure;
                    switch (f.Failure)
                    {
                        case FailType.Throttle:
                            return (new ThrottleMsg(Proto2Address(f.Address), Proto2Direction(f.Direction), f.RateMBit));
                        case FailType.Abort:
                            return (new DisconnectMsg(Proto2Address(f.Address), true));
                        case FailType.Disconnect:
                            return (new DisconnectMsg(Proto2Address(f.Address), false));
                        case FailType.Exit:
                            return (new TerminateMsg(new Right<bool, int>(f.ExitValue)));
                        case FailType.Shutdown:
                            return (new TerminateMsg(new Left<bool, int>(false)));
                        case FailType.ShutdownAbrupt:
                            return (new TerminateMsg(new Left<bool, int>(true)));
                    }
                }
                else if (w.HasAddr)
                {
                    var a = w.Addr;
                    if (a.HasAddr)
                    {
                        return (new AddressReply(new RoleName(a.Node), Proto2Address(a.Addr)));
                    }
                    return (new GetAddress(new RoleName(a.Node)));
                }
                else if (w.HasDone)
                {
                    return (Done.Instance);
                }
                else
                {
                    throw new ArgumentException(string.Format("wrong message {0}", message));
                }
            }

            throw new ArgumentException(string.Format("wrong message {0}", message));
        }
    }
}
