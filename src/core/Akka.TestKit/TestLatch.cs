//-----------------------------------------------------------------------
// <copyright file="TestLatch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Threading;
using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// <para>A count down latch that initially is closed. In order for it to become open <see cref="CountDown"/> must be called.
    /// By default one call is enough, but this can be changed by specifying the count in the constructor.</para>
    /// 
    /// <para>By default a timeout of 5 seconds is used.</para>
    /// <para>
    /// When created using <see cref="TestKitBase.CreateTestLatch">TestKit.CreateTestLatch</see> the default
    /// timeout from <see cref="TestKitSettings.DefaultTimeout"/> is used and all timeouts are dilated, i.e. multiplied by 
    /// <see cref="Akka.TestKit.TestKitSettings.TestTimeFactor"/>
    /// </para>
    /// Timeouts will always throw an exception.
    /// </summary>
    public class TestLatch
    {
        private readonly CountdownEvent _latch;
        private readonly Func<TimeSpan, TimeSpan> _dilate;
        private readonly TimeSpan _defaultTimeout;

        [Obsolete("This field will be removed. TestKit.TestKitSetting.DefaultTimeout is an alternative.")]
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);



        /// <summary>
        /// Initializes a new instance of the <see cref="TestLatch"/> class with count = 1, i.e. the 
        /// instance will become open after one call to <see cref="CountDown"/>.
        /// The default timeout is set to 5 seconds.
        /// </summary>
        public TestLatch()
            : this(1, TimeSpan.FromSeconds(5))
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestLatch"/> class with the specified count, i.e
        /// number of times <see cref="CountDown"/> must be called to make this instance become open.
        /// The default timeout is set to 5 seconds.
        /// </summary>
        public TestLatch(int count)
            : this(count, TimeSpan.FromSeconds(5))
        {
            //Intentionally left blank
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TestLatch"/> class with the specified count, i.e
        /// number of times <see cref="CountDown"/> must be called to make this instance become open.
        /// </summary>
        public TestLatch(int count, TimeSpan defaultTimeout)
        {
            _latch = new CountdownEvent(count);
            _defaultTimeout = defaultTimeout;
        }

        /// <summary>
        /// Creates a TestLatch with the specified dilate function, timeout and count. 
        /// Intended to be used by TestKit.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal TestLatch(Func<TimeSpan, TimeSpan> dilate, int count, TimeSpan defaultTimeout)
            :this(dilate, defaultTimeout,count)
        {
        }

        //This one exists to be available to inheritors
        protected TestLatch(Func<TimeSpan, TimeSpan> dilate, TimeSpan defaultTimeout, int count)
            : this(count, defaultTimeout)
        {
            _dilate = dilate;
        }

        [Obsolete("Use another constructor instead")]
        public TestLatch(ActorSystem system, int count = 1)
        {
            _latch = new CountdownEvent(count);
        }


        /// <summary>
        /// Gets a value indicating whether the latch is open.
        /// </summary>
        public bool IsOpen
        {
            get { return _latch.CurrentCount == 0; }
        }


        /// <summary>
        /// Count down the latch.
        /// </summary>
        public void CountDown()
        {
            _latch.Signal();
        }

        /// <summary>
        /// Make this instance become open.
        /// </summary>
        public void Open()
        {
            while(!IsOpen) CountDown();
        }

        /// <summary>
        /// Reset this instance to the initial count, making it become closed.
        /// </summary>
        public void Reset()
        {
            _latch.Reset();
        }

        /// <summary>
        /// Expects the latch to become open within the specified timeout. If the timeout is reached, a
        /// <see cref="TimeoutException"/> is thrown.
        /// <para>
        /// If this instance has been created using <see cref="TestKitBase.CreateTestLatch">TestKit.CreateTestLatch</see> 
        /// <paramref name="timeout"/> is dilated, i.e. multiplied by <see cref="Akka.TestKit.TestKitSettings.TestTimeFactor"/>
        /// </para>
        /// </summary>
        /// <exception cref="TimeoutException">Thrown when the timeout is reached</exception>
        /// <exception cref="ArgumentException">Thrown when a too large timeout has been specified</exception>
        public void Ready(TimeSpan timeout)
        {
            if(timeout == TimeSpan.MaxValue) throw new ArgumentException(string.Format("TestLatch does not support waiting for {0}", timeout));
            if(_dilate != null)
                timeout = _dilate(timeout);
            var opened = _latch.Wait(timeout);
            if(!opened) throw new TimeoutException(
                string.Format("Timeout of {0}", timeout));
        }

        /// <summary>
        /// Expects the latch to become open within the default timeout. If the timeout is reached, a
        /// <see cref="TimeoutException"/> is thrown.
        /// <para>If no timeout was specified when creating this instance, 5 seconds is used.</para>
        /// <para>If this instance has been created using <see cref="TestKitBase.CreateTestLatch">TestKit.CreateTestLatch</see> the default
        /// timeout from <see cref="TestKitSettings.DefaultTimeout"/> is used and dilated, i.e. multiplied by 
        /// <see cref="Akka.TestKit.TestKitSettings.TestTimeFactor"/>
        /// </para>
        /// </summary>
        /// <exception cref="TimeoutException">Thrown when the timeout is reached</exception>
        public void Ready()
        {
            Ready(_defaultTimeout);
        }

        [Obsolete("Use Ready instead. This method will be removed in future versions")]
        public void Result(TimeSpan atMost)
        {
            Ready(atMost);
        }

        #region Static methods

        [Obsolete("Use the constructor instead. This method will be removed in future versions")]
        public static TestLatch Apply(ActorSystem system, int count = 1)
        {
            return new TestLatch(count);
        }

        #endregion
    }
}

