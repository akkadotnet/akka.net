using System;
using Akka.Dispatch;

namespace Akka.TestKit
{
    public class CallingThreadDispatcher : MessageDispatcher
    {
        public static string Id = "akka.test.calling-thread-dispatcher";
        //TODO: Implement CallingThreadDispatcher. 
        public override void Schedule(Action run)
        {
            run();
        }
    }

}