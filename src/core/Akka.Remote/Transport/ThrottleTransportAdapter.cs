using System;
using Akka.Actor;

namespace Akka.Remote.Transport
{
    public class ThrottleTransportAdapter
    {
        public enum Direction
        {
            Send,
            Receive,
            Both
        }
    }

    public abstract class ThrottleMode
    {
        public abstract Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens);
        public abstract TimeSpan TimeToAvailable(long currentNanoTime, int tokens);
    }

    public class Blackhole : ThrottleMode
    {
        private Blackhole() { }
        private static readonly Blackhole _instance = new Blackhole();

        public static Blackhole Instance
        {
            get
            {
                return _instance;
            }
        }

        public override Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            return Tuple.Create<ThrottleMode, bool>(this, true);
        }

        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            return TimeSpan.Zero;
        }
    }

    public class Unthrottled : ThrottleMode
    {
        private Unthrottled() { }
        private static readonly Unthrottled _instance = new Unthrottled();

        public static Unthrottled Instance
        {
            get
            {
                return _instance;
            }
        }

        public override Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            return Tuple.Create<ThrottleMode, bool>(this, false);
        }

        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            return TimeSpan.Zero;
        }
    }

    sealed class TokenBucket : ThrottleMode
    {
        readonly int _capacity;
        readonly double _tokensPerSecond;
        readonly long _nanoTimeOfLastSend;
        readonly int _availableTokens;

        public TokenBucket(int capacity, double tokensPerSecond, long nanoTimeOfLastSend, int availableTokens)
        {
            _capacity = capacity;
            _tokensPerSecond = tokensPerSecond;
            _nanoTimeOfLastSend = nanoTimeOfLastSend;
            _availableTokens = availableTokens;
        }

        bool IsAvailable(long nanoTimeOfSend, int tokens)
        {
            if (tokens > _capacity && _availableTokens > 0)
                return true; // Allow messages larger than capacity through, it will be recorded as negative tokens
            return Math.Min(_availableTokens + TokensGenerated(nanoTimeOfSend), _capacity) >= tokens;
        }

        public override Tuple<ThrottleMode, bool> TryConsumeTokens(long nanoTimeOfSend, int tokens)
        {
            if (IsAvailable(nanoTimeOfSend, tokens))
            {
                return Tuple.Create<ThrottleMode, bool>(Copy(
                    nanoTimeOfLastSend: nanoTimeOfSend,
                    availableTokens: Math.Min(_availableTokens - tokens + TokensGenerated(nanoTimeOfSend), _capacity))
                    , true);
            }
            return Tuple.Create<ThrottleMode, bool>(this, false);
        }

        public override TimeSpan TimeToAvailable(long currentNanoTime, int tokens)
        {
            var needed = (tokens > _capacity ? 1 : tokens) - TokensGenerated(currentNanoTime);
            return TimeSpan.FromSeconds(needed / _tokensPerSecond);
        }

        int TokensGenerated(long nanoTimeOfSend)
        {
            return Convert.ToInt32((nanoTimeOfSend - nanoTimeOfSend) * 1000000 * _tokensPerSecond / 1000);
        }

        TokenBucket Copy(int? capacity = null, double? tokensPerSecond = null, long? nanoTimeOfLastSend = null, int? availableTokens = null)
        {
            return new TokenBucket(
                capacity ?? _capacity,
                tokensPerSecond ?? _tokensPerSecond,
                nanoTimeOfLastSend ?? _nanoTimeOfLastSend,
                availableTokens ?? _availableTokens);
        }

        private bool Equals(TokenBucket other)
        {
            return _capacity == other._capacity
                && _tokensPerSecond.Equals(other._tokensPerSecond)
                && _nanoTimeOfLastSend == other._nanoTimeOfLastSend
                && _availableTokens == other._availableTokens;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is TokenBucket && Equals((TokenBucket)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _capacity;
                hashCode = (hashCode * 397) ^ _tokensPerSecond.GetHashCode();
                hashCode = (hashCode * 397) ^ _nanoTimeOfLastSend.GetHashCode();
                hashCode = (hashCode * 397) ^ _availableTokens;
                return hashCode;
            }
        }

        public static bool operator ==(TokenBucket left, TokenBucket right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(TokenBucket left, TokenBucket right)
        {
            return !Equals(left, right);
        }
    }

    public sealed class SetThrottle
    {
        readonly Address _address;
        public Address Address { get { return _address; } }
        readonly ThrottleTransportAdapter.Direction _direction;
        public ThrottleTransportAdapter.Direction Direction { get { return _direction; } }
        readonly ThrottleMode _mode;
        public ThrottleMode Mode { get { return _mode; } }

        public SetThrottle(Address address, ThrottleTransportAdapter.Direction direction, ThrottleMode mode)
        {
            _address = address;
            _direction = direction;
            _mode = mode;
        }

        private bool Equals(SetThrottle other)
        {
            return Equals(_address, other._address) && _direction == other._direction && Equals(_mode, other._mode);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is SetThrottle && Equals((SetThrottle)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (_address != null ? _address.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)_direction;
                hashCode = (hashCode * 397) ^ (_mode != null ? _mode.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(SetThrottle left, SetThrottle right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(SetThrottle left, SetThrottle right)
        {
            return !Equals(left, right);
        }
    }
}
