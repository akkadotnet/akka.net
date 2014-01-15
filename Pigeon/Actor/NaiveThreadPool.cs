//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//namespace Pigeon.Actor
//{
//    public static class NaiveThreadPool
//    {
//        private static volatile int next;
//        private static SingleThreadWorker[] workers;
//        static NaiveThreadPool()
//        {

//            int threadCount = 4;
//            workers = new SingleThreadWorker[threadCount];
//            for (int i = 0; i < threadCount; i++)
//            {
//                workers[i] = new SingleThreadWorker();
//            }
//        }

//        public static void Schedule(Action<object> task)
//        {
//            workers[next++ % workers.Length].Schedule(task);
//        }
//    }

//    public class SingleThreadWorker
//    {
//        private readonly ConcurrentQueue<Action<object>> _queue = new ConcurrentQueue<Action<object>>();
//        private volatile bool _hasMoreTasks;
//        private volatile bool _running = true;
//        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
//        public SingleThreadWorker()
//        {
//            var thread = new Thread(Run)
//            {
//                IsBackground = true,
//                Name = "worker" + Guid.NewGuid(),
//            };

//            thread.Start();
//        }

//        private void Run()
//        {
//            while (_running)
//            {
//                _signal.WaitOne();
//                do
//                {
//                    _hasMoreTasks = false;

//                    Action<object> task;
//                    while (_queue.TryDequeue(out task) && _running)
//                    {
//                        task(null);
//                    }
//                    //wait a short while to let _hasMoreTasks to maybe be set to true
//                    //this avoids the roundtrip to the AutoResetEvent
//                    //that is, if there is intense pressure on the pool, we let some new
//                    //tasks have the chance to arrive and be processed w/o signaling
//                    //if (!_hasMoreTasks)
//                    //    Thread.Sleep(100);

//                    busy = false;

//                } while (_hasMoreTasks);
//            }
//        }

//        public void Schedule(Action<object> task)
//        {
//            _hasMoreTasks = true;
//            _queue.Enqueue(task);

//            SetSignal();
//        }

//        private volatile bool busy = false;
//        private void SetSignal()
//        {
//            if (!busy)
//            {
//                busy = true;
//                _signal.Set();
//            }
//        }
//    }
//}
