// -----------------------------------------------------------------------
// <copyright file="ValidatedBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using RemotePingPong.RealPayload;
using RemotePingPong.RealPayload.V2;

namespace RemotePingPong
{
    /// <summary>A distinct, opt-in protocol; historical benchmark actors keep their original behavior.</summary>
    internal static class ValidatedBenchmark
    {
        internal const int SequenceBase = 1_000_000_000;
        internal const int MaximumMessages = 1_000_000;
        internal static readonly TimeSpan ProgressTimeout = TimeSpan.FromSeconds(10);

        internal sealed record Agreement(Guid RunId, int Messages, int Window, int AckEvery, bool OneWay,
            int PayloadBytes, int Version = 1);
        internal sealed record Prepare;
        internal sealed record Ready(Agreement Settings);
        internal sealed record Credit(Guid RunId, int Received);
        internal sealed record Fence(Guid RunId);
        internal sealed record Receipt(Agreement Settings, int Received);
        internal sealed record Failed(string Reason);

        internal sealed class Outcome
        {
            private readonly TaskCompletionSource<long> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private Exception? _failure;
            internal Task<long> Task => _completion.Task;
            internal void Succeed(long count) => _completion.TrySetResult(count);
            internal void Fail(Exception exception)
            {
                Interlocked.CompareExchange(ref _failure, exception, null);
                _completion.TrySetException(exception);
            }
            internal void ThrowIfFailed()
            {
                if (Volatile.Read(ref _failure) is { } failure)
                    throw new InvalidOperationException("Validated benchmark failed, including errors observed after its receipt.", failure);
            }
        }

        internal static RealBenchmarkMessage[] PreparePayloads(long count)
        {
            if (count is < 1 or > MaximumMessages)
                throw new ArgumentOutOfRangeException(nameof(count), $"Validated runs require 1–{MaximumMessages} messages per client.");
            var canonical = RealPayloadFactory.CreateCanonical();
            var payloads = new RealBenchmarkMessage[(int)count];
            for (var i = 0; i < payloads.Length; i++)
                payloads[i] = canonical with { SequenceNumber = SequenceBase + i };
            return payloads;
        }

        private static bool HasCanonicalContent(RealBenchmarkMessage message, RealBenchmarkMessage canonical) =>
            message.TimestampTicks == canonical.TimestampTicks && message.Value.Equals(canonical.Value)
            && message.Flag == canonical.Flag && message.CorrelationId == canonical.CorrelationId
            && message.Device == canonical.Device && message.Readings.SequenceEqual(canonical.Readings);

        internal sealed class Receiver : UntypedActor
        {
            private readonly Agreement _settings;
            private readonly RealBenchmarkMessage _canonical = RealPayloadFactory.CreateCanonical();
            private IActorRef? _sender;
            private int _received;
            private bool _finished;
            private bool _failed;

            public Receiver(Agreement settings)
            {
                if (settings.Messages is < 1 or > MaximumMessages || settings.Window < 1 || settings.AckEvery < 1
                    || settings.AckEvery > settings.Window || settings.PayloadBytes < 1 || settings.Version != 1)
                    throw new ArgumentException("Invalid validated benchmark agreement.", nameof(settings));
                _settings = settings;
            }

            protected override void OnReceive(object message)
            {
                if (_failed)
                    return;
                if (message is Agreement agreement)
                {
                    if (_sender is not null || agreement != _settings)
                    {
                        Reject("Phase/parameter agreement mismatch or repeated agreement.");
                        return;
                    }
                    _sender = Sender;
                    Sender.Tell(new Ready(_settings));
                    return;
                }
                if (_sender is null || !Sender.Equals(_sender) || _finished)
                {
                    Reject("Data outside its agreed phase or after the final fence.");
                    return;
                }
                switch (message)
                {
                    case RealBenchmarkMessage payload:
                        if (_received >= _settings.Messages || payload.SequenceNumber != SequenceBase + _received
                            || !HasCanonicalContent(payload, _canonical))
                        {
                            Reject($"Invalid data sequence/content at {_received} (sequence {payload.SequenceNumber}).");
                            return;
                        }
                        _received++;
                        if (!_settings.OneWay)
                            Sender.Tell(payload);
                        else if (_received % _settings.AckEvery == 0 || _received == _settings.Messages)
                            Sender.Tell(new Credit(_settings.RunId, _received));
                        break;
                    case Fence fence when fence.RunId == _settings.RunId && _received == _settings.Messages:
                        _finished = true;
                        Sender.Tell(new Receipt(_settings, _received));
                        break;
                    default:
                        Reject("Unexpected message, foreign phase, or fence before every unique message arrived.");
                        break;
                }
            }

            private void Reject(string reason)
            {
                _failed = true;
                Sender.Tell(new Failed(reason));
                if (_sender is not null && !Sender.Equals(_sender))
                    _sender.Tell(new Failed(reason));
            }
        }

        internal sealed class SenderActor : UntypedActor
        {
            private readonly Agreement _settings;
            private readonly RealBenchmarkMessage[] _payloads;
            private readonly IActorRef _receiver;
            private readonly Outcome _completion;
            private IActorRef? _preparing;
            private bool _ready;
            private bool _running;
            private bool _fenced;
            private bool _failed;
            private int _sent;
            private int _acknowledged;

            public SenderActor(Agreement settings, RealBenchmarkMessage[] payloads, IActorRef receiver,
                Outcome completion)
            {
                if (settings.Messages != payloads.Length)
                    throw new ArgumentException("Prepared payload count differs from agreement.", nameof(payloads));
                _settings = settings;
                _payloads = payloads;
                _receiver = receiver;
                _completion = completion;
            }

            protected override void OnReceive(object message)
            {
                if (_failed)
                    return;
                if (_completion.Task.IsCompleted)
                {
                    if (message is not ReceiveTimeout)
                        Fail(new InvalidOperationException("Unexpected message after final receipt: " + message));
                    return;
                }
                switch (message)
                {
                    case Prepare when !_ready && _preparing is null:
                        _preparing = Sender;
                        Context.SetReceiveTimeout(ProgressTimeout);
                        _receiver.Tell(_settings);
                        return;
                    case Ready ready when Sender.Equals(_receiver) && _preparing is not null && ready.Settings == _settings:
                        _ready = true;
                        Context.SetReceiveTimeout(null);
                        _preparing.Tell(true);
                        _preparing = null;
                        return;
                    case Messages.Run when _ready && !_running:
                        _running = true;
                        Context.SetReceiveTimeout(ProgressTimeout);
                        SendAvailable();
                        return;
                    case Failed failed when Sender.Equals(_receiver):
                        Fail(new InvalidOperationException(failed.Reason));
                        return;
                    case ReceiveTimeout:
                        Fail(new TimeoutException($"Validated benchmark made no progress: sent={_sent}, confirmed={_acknowledged}. No retries were issued."));
                        return;
                }
                if (!_running || !Sender.Equals(_receiver))
                {
                    Fail(new InvalidOperationException("Unexpected sender or message outside the running phase."));
                    return;
                }
                switch (message)
                {
                    case Credit credit when _settings.OneWay && credit.RunId == _settings.RunId
                        && credit.Received > _acknowledged && credit.Received <= _sent
                        && (credit.Received % _settings.AckEvery == 0 || credit.Received == _settings.Messages):
                        _acknowledged = credit.Received;
                        SendAvailable();
                        break;
                    case RealBenchmarkMessage echo when !_settings.OneWay && _acknowledged < _sent
                        && echo.Equals(_payloads[_acknowledged]):
                        _acknowledged++;
                        SendAvailable();
                        break;
                    case Receipt receipt when _fenced && receipt.Settings == _settings
                        && receipt.Received == _settings.Messages && _acknowledged == _settings.Messages:
                        Context.SetReceiveTimeout(null);
                        _completion.Succeed(receipt.Received);
                        break;
                    default:
                        Fail(new InvalidOperationException("Invalid cumulative credit, echo sequence/content, or final receipt."));
                        break;
                }
            }

            private void SendAvailable()
            {
                while (_sent < _settings.Messages && _sent - _acknowledged < _settings.Window)
                {
                    _receiver.Tell(_payloads[_sent++]);
                }
                if (!_fenced && _sent == _settings.Messages && (_settings.OneWay || _acknowledged == _settings.Messages))
                {
                    _fenced = true;
                    _receiver.Tell(new Fence(_settings.RunId));
                }
            }

            private void Fail(Exception exception)
            {
                _failed = true;
                Context.SetReceiveTimeout(null);
                _preparing?.Tell(new Status.Failure(exception));
                _completion.Fail(exception);
            }

            protected override void PostStop()
            {
                if (!_completion.Task.IsCompleted)
                    Fail(new OperationCanceledException("Validated sender stopped before final delivery confirmation."));
                base.PostStop();
            }
        }
    }
}
