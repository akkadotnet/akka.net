using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.TestKit.Internal;

namespace Akka.TestKit;

public abstract partial class TestKitBase
{
    public IReadOnlyCollection<object> ExpectMsgAllOfMatchingPredicates(params PredicateInfo[] predicates)
    {
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        return ExpectMsgAllOfMatchingPredicates(predicates, cts.Token);
    }

    public IReadOnlyCollection<object> ExpectMsgAllOfMatchingPredicates(IReadOnlyCollection<PredicateInfo> predicates,
        CancellationToken cancellationToken)
    {
        return ExpectMsgAllOfMatchingPredicatesAsync(predicates, cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async IAsyncEnumerable<object> ExpectMsgAllOfMatchingPredicatesAsync(
        IReadOnlyCollection<PredicateInfo> predicates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerable = InternalExpectMsgAllOfMatchingPredicatesAsync(new TimeSpan(0, 0, 60), predicates,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false).WithCancellation(cancellationToken);
        await foreach (var item in enumerable)
        {
            yield return item;
        }
    }

    public IReadOnlyCollection<object> ExpectMsgAllOfMatchingPredicates(TimeSpan max, params PredicateInfo[] predicates)
    {
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        return ExpectMsgAllOfMatchingPredicates(max, predicates, cts.Token);
    }

    public IReadOnlyCollection<object> ExpectMsgAllOfMatchingPredicates(TimeSpan max,
        IReadOnlyCollection<PredicateInfo> predicates, CancellationToken cancellationToken)
    {
        return ExpectMsgAllOfMatchingPredicatesAsync(max, predicates, cancellationToken)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async IAsyncEnumerable<object> ExpectMsgAllOfMatchingPredicatesAsync(
        TimeSpan max,
        IReadOnlyCollection<PredicateInfo> predicates,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        max.EnsureIsPositiveFinite("max");
        var enumerable = InternalExpectMsgAllOfAsync(Dilated(max), predicates, cancellationToken: cancellationToken)
            .ConfigureAwait(false).WithCancellation(cancellationToken);
        await foreach (var item in enumerable)
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<object> InternalExpectMsgAllOfMatchingPredicatesAsync(
        TimeSpan max,
        IReadOnlyCollection<PredicateInfo> predicateInfos,
        bool shouldLog = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var predicateInfoList = predicateInfos.ToList();

        ConditionalLog(shouldLog, "Expecting {0} messages during {1}", predicateInfos.Count, max);
        var start = Now;

        var unexpectedMessages = new List<object>();
        var receivedMessages = InternalReceiveNAsync(predicateInfos.Count, max, shouldLog, cancellationToken)
            .ConfigureAwait(false).WithCancellation(cancellationToken);
        await foreach (var msg in receivedMessages)
        {
            var foundPredicateInfo = predicateInfoList.FirstOrDefault(p =>
                p.Type == msg.GetType() && (bool)p.PredicateT.DynamicInvoke(msg));
            if (foundPredicateInfo == null)
                unexpectedMessages.Add(msg);
            else
            {
                predicateInfoList.Remove(foundPredicateInfo);
            }

            yield return msg;
        }

        CheckMissingAndUnexpected(predicateInfoList, unexpectedMessages, "not found", "found unexpected", shouldLog,
            $"Expected {predicateInfos.Count} messages during {max}. Failed after {Now - start}. ");
    }
}

public class PredicateInfo
{
    public Type Type { get; }
    public Delegate PredicateT { get; }

    private PredicateInfo(Delegate predicateT, Type type)
    {
        Type = type;
        PredicateT = predicateT;
    }

    public static PredicateInfo Create<T>(Predicate<T> predicateT)
    {
        return new PredicateInfo(predicateT, typeof(T));
    }

    public override string ToString()
    {
        return $"{GetType().FullName}<{Type.FullName}>";
    }
}



public static class PredicateInfoFactory
{
    public static PredicateInfo CreatePredicateInfo<T>(this Predicate<T> predicateT)
    {
        return PredicateInfo.Create(predicateT);
    }

    public static PredicateInfo CreatePredicateInfo<T>(this Delegate predicateT)
    {
        var error = "";

        var method = predicateT.Method;
        if (method.ReturnType != typeof(bool))
        {
            error = "Return type of this Delegate is not a boolean. ";
        }

        var parameters = method.GetParameters();
        if (parameters.Length != 1)
        {
            error += "Delegate does not take exactly one parameter.";
        }

        if (error.Length != 0)
            throw new ArgumentException(error);

        var predicate =
            (Predicate<T>)Delegate.CreateDelegate(typeof(Predicate<T>), predicateT.Target, predicateT.Method);
        return PredicateInfo.Create(predicate);
    }
}
