namespace Akka.Streams.Util
{
    /// <summary>
    /// Allows tracking of whether a value has be initialized (even with the default value) for both
    /// reference and value types.
    /// Useful where distinguishing between null (or zero, or false) and unitialized is significant.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class Holder<T>
    {
        private T _value;

        public Holder()
        {
            Reset();
        }

        public Holder(T value)
        {
            Value = @value;
        }

        public T Value
        {
            get { return _value; }
            set
            {
                HasValue = true;
                _value = value;
            }
        }

        public bool HasValue { get; private set; }

        public void Reset()
        {
            _value = default(T);
            HasValue = false;
        }
    }
}