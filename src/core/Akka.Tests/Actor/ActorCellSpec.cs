//-----------------------------------------------------------------------
// <copyright file="ActorCellSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{

    public class ActorCellSpec : AkkaSpec
    {
        public class DummyActor : ReceiveActor
        {
            public DummyActor(AutoResetEvent autoResetEvent)
            {
                ReceiveAny(m => autoResetEvent.Set());
            }
        }

        public class DummyAsyncActor : ReceiveActor
        {
            public DummyAsyncActor(AutoResetEvent autoResetEvent)
            {
                ReceiveAsync<string>(async m =>
                {
                    await Task.Delay(500);
                    autoResetEvent.Set();
                });
            }
        }

        [Fact]
        public void Cell_should_clear_current_message_after_receive()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create(() => new DummyActor(autoResetEvent)));
            actor.Tell("hello");
            //ensure the message was received
            autoResetEvent.WaitOne();
            var refCell = actor as ActorRefWithCell;
            var cell = refCell.Underlying as ActorCell;
            //wait while current message is not null (that is, receive is not yet completed/exited)
            SpinWait.SpinUntil(() => cell.CurrentMessage == null, TimeSpan.FromSeconds(2));

            cell.CurrentMessage.ShouldBe(null);
        }

        [Fact]
        public void Cell_should_clear_current_message_after_async_receive()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);
            var actor = Sys.ActorOf(Props.Create(() => new DummyAsyncActor(autoResetEvent)));
            actor.Tell("hello");
            //ensure the message was received
            Assert.True(autoResetEvent.WaitOne(TimeSpan.FromSeconds(3)), "Timed out while waiting for autoreset event");
            var refCell = actor as ActorRefWithCell;
            var cell = refCell.Underlying as ActorCell;
            //wait while current message is not null (that is, receive is not yet completed/exited)
            SpinWait.SpinUntil(() => cell.CurrentMessage == null, TimeSpan.FromSeconds(2));

            cell.CurrentMessage.ShouldBe(null);
        }
    }
}
