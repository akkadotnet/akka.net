using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public interface IReadConsistency
    {
        TimeSpan Timeout { get; }
    }

    public class ReadLocal : IReadConsistency
    {
        static readonly ReadLocal _instance = new ReadLocal();

        public static ReadLocal Instance { get { return _instance; } }
        
        public TimeSpan Timeout
        {
            get { return TimeSpan.Zero; }
        }

        private ReadLocal()
        { }

        public override bool Equals(object obj)
        {
            return obj != null && obj is ReadLocal;
        }
    }

    public class ReadFrom : IReadConsistency
    {
        readonly int _n;
        readonly TimeSpan _timeout;

        public int N
        {
            get { return _n; }
        }

        public TimeSpan Timeout
        {
            get { return _timeout; }
        }

        public ReadFrom(int n, TimeSpan timeout)
        {
            _n = n;
            _timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadFrom;
            if(other != null)
            {
                return _n == other._n && _timeout.Equals(other._timeout);
            }
            return false;
        }
    }

    public class ReadMajority : IReadConsistency
    {
        readonly TimeSpan _timeout;

        public TimeSpan Timeout
        {
            get { return _timeout; }
        }

        public ReadMajority(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadMajority;
            if(other!=null)
            {
                return _timeout == other._timeout;
            }
            return false;
        }
    }

    public class ReadAll : IReadConsistency
    {
        readonly TimeSpan _timeout;

        public TimeSpan Timeout
        {
            get { return _timeout; }
        }

        public ReadAll(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public override bool Equals(object obj)
        {
            var other = obj as ReadAll;
            if(other != null)
            {
                return _timeout == other._timeout;
            }
            return false;
        }
    }

}
