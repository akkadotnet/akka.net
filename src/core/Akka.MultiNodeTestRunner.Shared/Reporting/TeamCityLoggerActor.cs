using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.MultiNodeTestRunner.Shared.Reporting
{
    public class TeamCityLoggerActor : ReceiveActor
    {
        private readonly bool _unMuted = false;
        public TeamCityLoggerActor(bool unMuted)
        {
            _unMuted = unMuted;
            
            ReceiveAny(o =>
            {
                if (_unMuted)
                {
                    Console.WriteLine(o.ToString());
                }
            });
        }
    }
}
