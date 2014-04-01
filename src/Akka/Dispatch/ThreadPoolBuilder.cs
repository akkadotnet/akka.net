using System;
using Akka.Configuration;

namespace Akka.Dispatch
{
    class ThreadPoolBuilder
    {
    }

    class ThreadPoolConfig
    {
        private readonly Config _config;

        public ThreadPoolConfig(Config config)
        {
            _config = config;
        }

        public int PoolSizeMin
        {
            get { return _config.GetInt("pool-size-min"); }
        }

        public double PoolSizeFactor
        {
            get { return _config.GetDouble("pool-size-factor"); }
        }

        public int PoolSizeMax
        {
            get { return _config.GetInt("pool-size-max"); }
        }

        #region Static methods

        public static int ScaledPoolSize(int floor, double scalar, int ceiling)
        {
            return Math.Min(Math.Max((int) (Environment.ProcessorCount*scalar), floor), ceiling);
        }

        #endregion
    }
}
