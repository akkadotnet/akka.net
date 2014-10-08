using System;

namespace Akka.Event
{
    public abstract class LoggingAdapterBase : LoggingAdapter
    {
        private readonly ILogMessageFormatter _logMessageFormatter;


        public abstract bool IsDebugEnabled { get; }


        public abstract bool IsErrorEnabled { get; }


        public abstract bool IsInfoEnabled { get; }


        public abstract bool IsWarningEnabled { get; }


        protected abstract void NotifyError(object message);


        protected abstract void NotifyError(Exception cause, object message);


        protected abstract void NotifyWarning(object message);


        protected abstract void NotifyInfo(object message);


        protected abstract void NotifyDebug(object message);

        protected LoggingAdapterBase(ILogMessageFormatter logMessageFormatter)
        {
            if(logMessageFormatter == null)
                throw new ArgumentException("logMessageFormatter");

            _logMessageFormatter = logMessageFormatter;
        }


        public bool IsEnabled(LogLevel logLevel)
        {
            switch(logLevel)
            {
                case LogLevel.DebugLevel:
                    return IsDebugEnabled;
                case LogLevel.InfoLevel:
                    return IsInfoEnabled;
                case LogLevel.WarningLevel:
                    return IsWarningEnabled;
                case LogLevel.ErrorLevel:
                    return IsErrorEnabled;
                default:
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }


        protected void NotifyLog(LogLevel logLevel, object message)
        {
            switch(logLevel)
            {
                case LogLevel.DebugLevel:
                    if(IsDebugEnabled) NotifyDebug(message);
                    break;
                case LogLevel.InfoLevel:
                    if(IsInfoEnabled) NotifyInfo(message);
                    break;
                case LogLevel.WarningLevel:
                    if(IsWarningEnabled) NotifyWarning(message);
                    break;
                case LogLevel.ErrorLevel:
                    if(IsErrorEnabled) NotifyError(message);
                    break;
                default:
                    throw new NotSupportedException("Unknown LogLevel " + logLevel);
            }
        }


        public void Debug(string format, params object[] args)
        {
            if(IsDebugEnabled)
            {
                if(args == null || args.Length == 0)
                    NotifyDebug(format);
                else
                    NotifyDebug(new LogMessage(_logMessageFormatter, format, args));
            }
        }

        [Obsolete("Use Warning instead")]
        public void Warn(string format, params object[] args)
        {
            Warning(format, args);
        }

        public void Warning(string format, params object[] args)
        {
            if(IsWarningEnabled)
                if(args == null || args.Length == 0)
                    NotifyWarning(format);
                else
                    NotifyWarning(new LogMessage(_logMessageFormatter, format, args));
        }

        public void Error(Exception cause, string format, params object[] args)
        {
            if(IsErrorEnabled)
                if(args == null || args.Length == 0)
                    NotifyError(cause, format);
                else
                    NotifyError(cause, new LogMessage(_logMessageFormatter, format, args));
        }

        public void Error(string format, params object[] args)
        {
            if(IsErrorEnabled)
                if(args == null || args.Length == 0)
                    NotifyError(format);
                else
                    NotifyError(new LogMessage(_logMessageFormatter, format, args));
        }

        public void Info(string format, params object[] args)
        {
            if(IsInfoEnabled)
                if(args == null || args.Length == 0)
                    NotifyInfo(format);
                else
                    NotifyInfo(new LogMessage(_logMessageFormatter, format, args));
        }

        public void Log(LogLevel logLevel, string format, params object[] args)
        {
            if(args == null || args.Length == 0)
                NotifyLog(logLevel, format);
            else
                NotifyLog(logLevel, new LogMessage(_logMessageFormatter, format, args));
        }
    }
}