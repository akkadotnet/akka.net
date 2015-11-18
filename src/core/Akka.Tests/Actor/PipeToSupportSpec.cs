//-----------------------------------------------------------------------
// <copyright file="PipeToSupportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class PipeToSupportSpec : AkkaSpec
    {
        private TaskCompletionSource<string> _taskCompletionSource;
        private Task<string> _task;

        public PipeToSupportSpec()
        {
            _taskCompletionSource = new TaskCompletionSource<string>();
            _task = _taskCompletionSource.Task;
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
        }

        [Fact]
        public void Should_by_default_send_task_result_as_message()
        {
            _task.PipeTo(TestActor);
            _taskCompletionSource.SetResult("Hello");
            ExpectMsg("Hello");
        }

        [Fact]
        public void Should_by_default_send_task_exception_as_status_failure_message()
        {
            _task.PipeTo(TestActor);
            _taskCompletionSource.SetException(new Exception("Boom"));
            ExpectMsg<Status.Failure>(x => x.Cause.InnerException.Message == "Boom");
        }

        [Fact]
        public void Should_use_success_handling_to_transform_task_result()
        {
            _task.PipeTo(TestActor, success: x => "Hello " + x);
            _taskCompletionSource.SetResult("World");
            ExpectMsg("Hello World");
        }

        [Fact]
        public void Should_use_failure_handling_to_transform_task_exception()
        {
            _task.PipeTo(TestActor, failure: e => "Such a " + e.InnerException.Message);
            _taskCompletionSource.SetException(new Exception("failure..."));
            ExpectMsg("Such a failure...");
        }
    }
}