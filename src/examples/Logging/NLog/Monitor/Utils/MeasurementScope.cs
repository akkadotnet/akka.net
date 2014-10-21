using Monitor.Actors.Events;
using System;
using System.Diagnostics;

namespace Monitor.Utils
{
    /// <summary>
    /// The MeasurementScope uses a StopWatch to measure how long it takes
    /// to Execute an action.
    /// </summary>
    public sealed class MeasurementScope : IDisposable
    {
        private Action<Uri, TimeSpan> _action;
        private Action<MonitoringResult> _onFinish;

        public MeasurementScope OnFinish(Action<MonitoringResult> onFinish)
        {
            _onFinish = onFinish;
            return this;
        }

        public static MeasurementScope Execute(Action<Uri, TimeSpan> action)
        {
            var scope = new MeasurementScope { _action = action };
            return scope;
        }

        /// <summary>
        /// Runs the scope and measures the total execution time (including situations when an exception is thrown).
        /// Delegates the handling of TException to the caller. Does not rethrow.
        /// </summary>
        /// <typeparam name="TException"></typeparam>
        /// <param name="uri"></param>
        /// <param name="timeout"></param>
        public void Run<TException>(Uri uri, TimeSpan timeout)
            where TException : Exception
        {
            var error = false;
            var timer = new Stopwatch();
            TException exception = null;

            timer.Start();
            try
            {
                _action(uri, timeout);
            }
            catch (TException ex)
            {
                error = true;
                exception = ex;
            }
            finally
            {
                timer.Stop();
            }

            var result = error ? new MonitoringResult(uri, exception, timer.Elapsed) : new MonitoringResult(uri, timer.Elapsed);

            if (_onFinish != null)
                _onFinish(result);
        }

        public void Dispose()
        {
            _action = null;
            _onFinish = null;
            GC.SuppressFinalize(this);
        }
    }
}