using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class LoggingAdapter 
    {
        public void Info(string text)
        {
            //TODO: implement
            //TODO: should this java api be used or replaced with tracewriter or somesuch?
            Trace.WriteLine(text);
        }
    }

    public static class Logging
    {
        public static LoggingAdapter GetLogger(ActorSystem system)
        {
            var actor = ActorCell.Current.Actor;
            return new LoggingAdapter();
        }
    }
}
