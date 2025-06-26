//-----------------------------------------------------------------------
// <copyright file="ResultSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Util;
using FluentAssertions;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Tests.Util;

public class ResultSpec
{
    [Fact(DisplayName = "Result constructor with value should return success")]
    public void SuccessfulResult()
    {
        var result = new Result<int>(1);
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        result.Exception.Should().BeNull();
    }
    
    [Fact(DisplayName = "Result constructor with exception should return failed")]
    public void ExceptionResult()
    {
        var result = new Result<int>(new TestException("BOOM"));
        
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<TestException>();
    }
    
    [Fact(DisplayName = "Result.Success with value should return success")]
    public void SuccessfulStaticSuccess()
    {
        var result = Result.Success(1);
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        result.Exception.Should().BeNull();
    }
    
    [Fact(DisplayName = "Result.Failure with exception should return failed")]
    public void ExceptionStaticFailure()
    {
        var result = Result.Failure<int>(new TestException("BOOM"));
        
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<TestException>();
    }
    
    [Fact(DisplayName = "Result.From with successful Func should return success")]
    public void SuccessfulFuncResult()
    {
        var result = Result.From(() => 1);
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        result.Exception.Should().BeNull();
    }
    
    [Fact(DisplayName = "Result.From with throwing Func should return failed")]
    public void ThrowFuncResult()
    {
        var result = Result.From<int>(() => throw new TestException("BOOM"));
        
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<TestException>();
    }
    
    [Fact(DisplayName = "Result.FromTask with successful task should return success")]
    public void SuccessfulTaskResult()
    {
        var task = CompletedTask(1);
        var result = Result.FromTask(task);
        
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        result.Exception.Should().BeNull();
    }
    
    [Fact(DisplayName = "Result.FromTask with faulted task should return failed")]
    public void FaultedTaskResult()
    {
        var task = FaultedTask(1);
        var result = Result.FromTask(task);
        
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<AggregateException>()
            .Which.InnerException.Should().BeOfType<TestException>();
    }
    
    [Fact(DisplayName = "Result.FromTask with cancelled task should return failed")]
    public void CancelledTaskResult()
    {
        var task = CancelledTask(1);
        var result = Result.FromTask(task);
        
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception.Should().BeOfType<TaskCanceledException>();
    }
    
    [Fact(DisplayName = "Result.FromTask with incomplete task should throw")]
    public void IncompleteTaskResult()
    {
        var tcs = new TaskCompletionSource<int>();
        Invoking(() => Result.FromTask(tcs.Task))
            .Should().Throw<ArgumentException>().WithMessage("Task is not completed.*");
    }
    
    [Fact]
    public void ResultEqualitySpec()
    {
        var result1 = Result.Success(1);
        var result2 = Result.Success(1);
        var exception = new TestException("BOOM"); // equality by value does not work for exceptions
        var result3 = Result.Failure<int>(exception);
        var result4 = Result.Failure<int>(exception);
        var result5 = Result.Success("foo");
        var result6 = Result.Success("bar");
        var result51 = Result.Success("foo");
        
        result1.Equals(result2).Should().BeTrue();
        result1.Equals(result3).Should().BeFalse();
        result3.Equals(result4).Should().BeTrue();
        result5.Equals(result51).Should().BeTrue();
        (result5 == result51).Should().BeTrue(); // test operator overloads
        (result5 == result6).Should().BeFalse(); // test operator overloads
        (result5 != result6).Should().BeTrue(); // test operator overloads
    }
    
    private static Task<int> CompletedTask(int n)
    {
        var tcs = new TaskCompletionSource<int>();
        Task.Run(async () =>
        {
            await Task.Yield();
            tcs.TrySetResult(n);
        });
        tcs.Task.Wait();
        return tcs.Task;
    }
    
    private static Task<int> CancelledTask(int n)
    {
        var tcs = new TaskCompletionSource<int>();
        Task.Run(async () =>
        {
            await Task.Yield();
            tcs.TrySetCanceled();
        });

        try
        {
            tcs.Task.Wait();
        }
        catch
        {
            // no-op
        }
        
        return tcs.Task;
    }

    private static Task<int> FaultedTask(int n)
    {
        var tcs = new TaskCompletionSource<int>();
        Task.Run(async () =>
        {
            await Task.Yield();
            try
            {
                throw new TestException("BOOM");
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        
        try
        {
            tcs.Task.Wait();
        }
        catch 
        {
            // no-op
        }
        
        return tcs.Task;
    }

    private class TestException: Exception
    {
        public TestException(string message) : base(message)
        {
        }

        public TestException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
