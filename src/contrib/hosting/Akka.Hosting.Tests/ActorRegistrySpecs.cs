using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.Tests;

public class ActorRegistrySpecs
{
    [Fact]
    public void Should_throw_upon_duplicate_registration()
    {
        // arrange
        var registry = new ActorRegistry();
        registry.Register<Nobody>(Nobody.Instance);
        
        // act

        var register = () => registry.Register<Nobody>(Nobody.Instance);

        // assert
        register.Should().Throw<DuplicateActorRegistryException>();
    }
    
    [Fact]
    public void Should_not_throw_upon_duplicate_registration_when_overwrite_allowed()
    {
        // arrange
        var registry = new ActorRegistry();
        registry.Register<Nobody>(Nobody.Instance);
        
        // act

        var register = () => registry.Register<Nobody>(Nobody.Instance, true);

        // assert
        register.Should().NotThrow<DuplicateActorRegistryException>();
    }
    
    [Fact]
    public void Should_throw_NullReferenceException_for_Null_IActorRef()
    {
        // arrange
        var registry = new ActorRegistry();

        // act

        var register = () => registry.Register<Nobody>(null!); // intentionally null for the test

        // assert
        register.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Should_throw_on_missing_entry_during_Get()
    {
        // arrange
        var registry = new ActorRegistry();
        
        // assert
        registry.Invoking(x => x.Get<Nobody>()).Should().Throw<MissingActorRegistryEntryException>();
    }
    
    [Fact]
    public void Should_not_throw_on_missing_entry_during_TryGet()
    {
        // arrange
        var registry = new ActorRegistry();
        
        // assert
        registry.Invoking(x => x.TryGet<Nobody>(out var actor)).Should().NotThrow<MissingActorRegistryEntryException>();
    }

    [Fact]
    public async Task Should_complete_GetAsync_upon_KeyRegistered()
    {
        // arrange
        var registry = new ActorRegistry();
        
        // act
        var task = registry.GetAsync<Nobody>();
        task.IsCompletedSuccessfully.Should().BeFalse();
        
        registry.Register<Nobody>(Nobody.Instance);
        var result = await task;

        // assert
        result.Should().Be(Nobody.Instance);
    }
    
    [Fact]
    public async Task Should_complete_multiple_GetAsync_upon_KeyRegistered()
    {
        // arrange
        var registry = new ActorRegistry();
        
        // act
        var task1 = registry.GetAsync<Nobody>();
        var task2 = registry.GetAsync<Nobody>();
        var task3 = registry.GetAsync<Nobody>();

        // validate that all three tasks are distinct
        task1.Should().NotBe(task2).And.NotBe(task3);

        var aggregate = Task.WhenAll(task1, task2, task3);

        registry.Register<Nobody>(Nobody.Instance);
        var result = await aggregate;

        // assert
        result.First().Should().Be(Nobody.Instance);
    }
    
    [Fact]
    public void GetAsync_should_return_CompletedTask_if_Key_AlreadyExists()
    {
        // arrange
        var registry = new ActorRegistry();
        registry.Register<Nobody>(Nobody.Instance);
        
        // act
        var task = registry.GetAsync<Nobody>();

        // assert
        task.IsCompletedSuccessfully.Should().BeTrue();
    }
    
    [Fact]
    public async Task GetAsync_should_Cancel_after_Timeout()
    {
        // arrange
        var registry = new ActorRegistry();
        var cancellationTokenSource = new CancellationTokenSource();

        // act
        var task = registry.GetAsync<Nobody>(cancellationTokenSource.Token);
        // assert
        await Awaiting(async () =>
        {
            cancellationTokenSource.Cancel();
            await task.WaitAsync(TimeSpan.FromSeconds(3));
        }).Should().ThrowAsync<TaskCanceledException>();
    }
}