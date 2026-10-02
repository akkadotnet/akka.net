// -----------------------------------------------------------------------
//  <copyright file="FailureDetectorOptions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Hosting;

namespace Akka.Remote.Hosting;

/// <summary>
///     This failure detector is usually used to detect TCP network connection failure.
///     For TCP it is not important to have fast failure detection, since
///     most connection failures are captured by TCP itself.
///     The default value will trigger if there are no heartbeats within
///     the duration heartbeat-interval + acceptable-heartbeat-pause, i.e. 124 seconds
/// </summary>
public class DeadlineFailureDetectorOptions
{
    /// <summary>
    ///     <para>
    ///         The interval between each heartbeat messages that are sent to each connection.
    ///     </para>
    ///     <b>Default:</b> 4 seconds 
    /// </summary>
    public TimeSpan? HeartbeatInterval { get; set; }

    /// <summary>
    ///     <para>
    ///         Number of potentially lost/delayed heartbeats that will be
    ///         accepted before considering it to be an anomaly.
    ///         This margin is important to be able to survive sudden, occasional,
    ///         pauses in heartbeat arrivals, due to for example garbage collect or
    ///         network drop.
    ///     </para>
    ///     <b>Default:</b> 120 seconds 
    /// </summary>
    public TimeSpan? AcceptableHeartbeatPause { get; set; }
    
    public StringBuilder ToHocon()
    {
        var sb = new StringBuilder();
        
        if(HeartbeatInterval is not null)
            sb.AppendLine($"heartbeat-interval = {HeartbeatInterval.ToHocon()}");
        if(AcceptableHeartbeatPause is not null)
            sb.AppendLine($"acceptable-heartbeat-pause = {AcceptableHeartbeatPause.ToHocon()}");
        
        return sb;
    }
}

/// <summary>
/// This failure detector is usually used for remote death watch.
/// It is based on Phi Accrual Failure Detector (http://ddg.jaist.ac.jp/pub/HDY+04.pdf
/// [Hayashibara et al])
/// </summary>
public class PhiAccrualFailureDetectorOptions
{
    /// <summary>
    ///     <para>
    ///         The interval between each heartbeat messages that are sent to each connection.
    ///     </para>
    ///     <b>Default:</b> 1 seconds 
    /// </summary>
    public TimeSpan? HeartbeatInterval { get; set; }

    /// <summary>
    ///     <para>
    ///         Number of potentially lost/delayed heartbeats that will be
    ///         accepted before considering it to be an anomaly.
    ///         This margin is important to be able to survive sudden, occasional,
    ///         pauses in heartbeat arrivals, due to for example garbage collect or
    ///         network drop.
    ///     </para>
    ///     <b>Default:</b> 10 seconds 
    /// </summary>
    public TimeSpan? AcceptableHeartbeatPause { get; set; }

    /// <summary>
    ///     <para>
    ///         Defines the failure detector threshold.
    ///         A low threshold is prone to generate many wrong suspicions but ensures
    ///         a quick detection in the event of a real crash. Conversely, a high
    ///         threshold generates fewer mistakes but needs more time to detect
    ///         actual crashes.
    ///     </para>
    ///     <b>Default:</b> 10.0
    /// </summary>
    public double? Threshold { get; set; }

    /// <summary>
    ///     <para>
    ///         Number of the samples of inter-heartbeat arrival times to adaptively
    ///         calculate the failure timeout for connections.
    ///     </para>
    ///     <b>Default:</b> 200
    /// </summary>
    public int? MaxSampleSize { get; set; }

    /// <summary>
    ///     <para>
    ///         Minimum standard deviation to use for the normal distribution in
    ///         <see cref="PhiAccrualFailureDetector"/>. Too low standard deviation might result in
    ///         too much sensitivity for sudden, but normal, deviations in heartbeat
    ///         inter arrival times.
    ///     </para>
    ///     <b>Default:</b> 100 milliseconds
    /// </summary>
    public TimeSpan? MinStandardDeviation { get; set; }

    /// <summary>
    ///     <para>
    ///         Interval between unreachable node check by the failure detector
    ///     </para>
    ///     <b>Default:</b> 1 second
    /// </summary>
    public TimeSpan? UnreachableNodesReaperInterval { get; set; }

    /// <summary>
    ///     <para>
    ///         After the heartbeat request has been sent the first failure detection
    ///         will start after this period, even though no heartbeat message has
    ///         been received.
    ///     </para>
    ///     <b>Default:</b> 1 second
    /// </summary>
    public TimeSpan? ExpectedResponseAfter { get; set; }
    
    public StringBuilder ToHocon()
    {
        var sb = new StringBuilder();
        
        if(HeartbeatInterval is not null)
            sb.AppendLine($"heartbeat-interval = {HeartbeatInterval.ToHocon()}");
        if(AcceptableHeartbeatPause is not null)
            sb.AppendLine($"acceptable-heartbeat-pause = {AcceptableHeartbeatPause.ToHocon()}");
        if(Threshold is not null)
            sb.AppendLine($"threshold = {Threshold.ToHocon()}");
        if(MaxSampleSize is not null)
            sb.AppendLine($"max-sample-size = {MaxSampleSize.ToHocon()}");
        if(MinStandardDeviation is not null)
            sb.AppendLine($"min-std-deviation = {MinStandardDeviation.ToHocon()}");
        if(UnreachableNodesReaperInterval is not null)
            sb.AppendLine($"unreachable-nodes-reaper-interval = {UnreachableNodesReaperInterval.ToHocon()}");
        if(ExpectedResponseAfter is not null)
            sb.AppendLine($"expected-response-after = {ExpectedResponseAfter.ToHocon()}");
        
        return sb;
    }
}