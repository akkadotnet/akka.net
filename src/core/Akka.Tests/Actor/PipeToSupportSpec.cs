//-----------------------------------------------------------------------
// <copyright file="PipeToSupportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Actor
{
    public class PipeToSupportSpec : AkkaSpec
    {
        private readonly TaskCompletionSource<string> _taskCompletionSource;
        private readonly Task<string> _task;
        private readonly Task _taskWithoutResult;

        private readonly ValueTask<string> _valueTask;
        private readonly ValueTask _valueTaskWithoutResult;

        public PipeToSupportSpec()
        {
            _taskCompletionSource = new TaskCompletionSource<string>();
            _task = _taskCompletionSource.Task;
            _taskWithoutResult = _taskCompletionSource.Task;

            _valueTask = new ValueTask<string>(_taskCompletionSource.Task);
            _valueTaskWithoutResult = new ValueTask(_taskCompletionSource.Task);
            
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
        }
        
        [Fact]
        public async Task Should_immediately_PipeTo_completed_Task()
        {
            var task = Task.FromResult("foo");
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            task.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await ExpectMsgAsync("foo");
        }

        [Fact]
        public async Task ValueTask_Should_immediately_PipeTo_completed_Task()
        {
            var task = new ValueTask<string>("foo");
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            task.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await ExpectMsgAsync("foo");
        }

        [Fact]
        public async Task Should_by_default_send_task_result_as_message()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _task.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetResult("Hello");
            await ExpectMsgAsync("Hello");
        }

        [Fact]
        public async Task ValueTask_Should_by_default_send_task_result_as_message()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTask.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetResult("Hello");
            await ExpectMsgAsync("Hello");
        }

        [Fact]
        public async Task Should_by_default_not_send_a_success_message_if_the_task_does_not_produce_a_result()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskWithoutResult.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetResult("Hello");
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task ValueTask_Should_by_default_not_send_a_success_message_if_the_task_does_not_produce_a_result()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTaskWithoutResult.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetResult("Hello");
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public async Task Should_by_default_send_task_exception_as_status_failure_message()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _task.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskWithoutResult.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetException(new Exception("Boom"));
            await ExpectMsgAsync<Status.Failure>(x => x.Cause.Message == "Boom");
            await ExpectMsgAsync<Status.Failure>(x => x.Cause.Message == "Boom");
        }

        [Fact]
        public async Task ValueTask_Should_by_default_send_task_exception_as_status_failure_message()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTask.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTaskWithoutResult.PipeTo(TestActor);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetException(new Exception("Boom"));
            await ExpectMsgAsync<Status.Failure>(x => x.Cause.Message == "Boom");
            await ExpectMsgAsync<Status.Failure>(x => x.Cause.Message == "Boom");
        }

        [Fact]
        public async Task Should_use_success_handling_to_transform_task_result()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _task.PipeTo(TestActor, success: x => "Hello " + x);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskWithoutResult.PipeTo(TestActor, success: () => "Hello");
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetResult("World");
            var pipeTo = await ReceiveNAsync(2, default).Cast<string>().ToListAsync();
            pipeTo.Should().Contain("Hello");
            pipeTo.Should().Contain("Hello World");
        }

        [Fact]
        public async Task ValueTask_Should_use_success_handling_to_transform_task_result()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTask.PipeTo(TestActor, success: x => "Hello " + x);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTaskWithoutResult.PipeTo(TestActor, success: () => "Hello");
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetResult("World");
            var pipeTo = await ReceiveNAsync(2, default).Cast<string>().ToListAsync();
            pipeTo.Should().Contain("Hello");
            pipeTo.Should().Contain("Hello World");
        }

        [Fact]
        public async Task Should_use_failure_handling_to_transform_task_exception()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _task.PipeTo(TestActor, failure: e => "Such a " + e.Message);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskWithoutResult.PipeTo(TestActor, failure: e => "Such a " + e.Message);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetException(new Exception("failure..."));
            await ExpectMsgAsync("Such a failure...");
            await ExpectMsgAsync("Such a failure...");
        }

        [Fact]
        public async Task ValueTask_Should_use_failure_handling_to_transform_task_exception()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTask.PipeTo(TestActor, failure: e => "Such a " + e.Message);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _valueTaskWithoutResult.PipeTo(TestActor, failure: e => "Such a " + e.Message);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _taskCompletionSource.SetException(new Exception("failure..."));
            await ExpectMsgAsync("Such a failure...");
            await ExpectMsgAsync("Such a failure...");
        }
    }
}
