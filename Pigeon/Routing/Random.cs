using Pigeon.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    //TODO: there has to be a better way to do this?
    public class ThreadSafeRandom
    {
        private static readonly Random _global = new Random();
        [ThreadStatic]
        private static Random _local;

        static ThreadSafeRandom()
        {
            if (_local == null)
            {
                int seed;
                lock (_global)
                {
                    seed = _global.Next();
                }
                _local = new Random(seed);
            }
        }
        public static int Next(int maxValue)
        {
            return _local.Next(maxValue);
        }
    }

    public class RandomLogic : RoutingLogic
    {
        public override Routee Select(object message, Routee[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                return Routee.NoRoutee;
            }
            else
            {
                return routees[ThreadSafeRandom.Next(routees.Length-1) % routees.Length];
            }
        }
    }

    public class RandomGroup : Group
    {
        public RandomGroup(Config config)
            : base(config.GetStringList("routees.paths"))
        {

        }
        public RandomGroup(params string[] paths)
            : base(paths)
        {

        }
        public RandomGroup(IEnumerable<string> paths)
            : base(paths)
        {

        }

        public override RoutingLogic GetLogic()
        {
            return new RoundRobinLogic();
        }
    }
}
