using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public interface IWriteConsistency
    {
        TimeSpan Timeout { get; }
    }

    public class WriteLocal : IWriteConsistency
    {
        static readonly WriteLocal _instance = new WriteLocal();

        static WriteLocal Instance { get { return _instance; } }

        public TimeSpan Timeout
        {
            get { return TimeSpan.Zero; }
        }
    }

    public class WriteTo : IReadConsistency
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

        public WriteTo(int n, TimeSpan timeout)
        {
            if(n < 2)
            {
                throw new ArgumentException("WriteTo requires n > 2, Use WriteLocal for n=1");
            }
            _n = n;
            _timeout = timeout;
        }
    }

    public class WriteMajority : IReadConsistency
    {
        readonly TimeSpan _timeout;

        public TimeSpan Timeout
        {
            get { return _timeout; }
        }

        public WriteMajority(TimeSpan timeout)
        {
            _timeout = timeout;
        }
    }

    public class WriteAll : IReadConsistency
    {
        readonly TimeSpan _timeout;

        public TimeSpan Timeout
        {
            get { return _timeout; }
        }

        public WriteAll(TimeSpan timeout)
        {
            _timeout = timeout;
        }
    }

}
