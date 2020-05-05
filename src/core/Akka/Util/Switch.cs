//-----------------------------------------------------------------------
// <copyright file="Switch.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util
{
    /// <summary>
    /// An atomic switch that can be either on or off
    /// </summary>
    public class Switch
    {
        private readonly Util.AtomicBoolean _switch;
        private readonly object _lock = new object();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="startAsOn">TBD</param>
        public Switch(bool startAsOn = false)
        {
            _switch = new Util.AtomicBoolean(startAsOn);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="from">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        protected bool TranscendFrom(bool from, Action action)
        {
            lock(_lock)
            {
                if(_switch.CompareAndSet(from, !from))
                {
                    try
                    {
                        action();
                    }
                    catch(Exception)
                    {
                        _switch.CompareAndSet(!from, from); // revert status
                        throw;
                    }
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Executes the provided action if the lock is on. This is done under a lock so be _very_ careful with longrunning/blocking operations in it.
        /// Only executes the action if the switch is on, and switches it off immediately after obtaining the lock.
        /// Will switch it back on if the provided action throws an exception.
        /// </summary>
        /// <param name="action">TBD</param>
        /// <returns>Returns <c>true</c> if the switch was switched off</returns>
        public bool SwitchOff(Action action)
        {
            return TranscendFrom(true, action);
        }

        /// <summary>
        /// Executes the provided action if the lock is off. This is done under a lock so be _very_ careful with longrunning/blocking operations in it.
        /// Only executes the action if the switch is off, and switches it on immediately after obtaining the lock.
        /// Will switch it back off if the provided action throws an exception.
        /// </summary>
        /// <param name="action">TBD</param>
        /// <returns>Returns <c>true</c> if the switch was switched on</returns>
        public bool SwitchOn(Action action)
        {
            return TranscendFrom(false, action);
        }

        /// <summary>
        /// Switches the switch off (if on). Uses locking.
        /// </summary>
        /// <returns>Returns <c>true</c> if the switch was switched off</returns>
        public bool SwitchOff()
        {
            lock(_lock)
            {
                return _switch.CompareAndSet(true, false);
            }
        }


        /// <summary>
        /// Switches the switch on (if off). Uses locking.
        /// </summary>
        /// <returns>Returns <c>true</c> if the switch was switched on</returns>
        public bool SwitchOn()
        {
            lock(_lock)
            {
                return _switch.CompareAndSet(false, true);
            }
        }

        /// <summary>
        /// Executes the provided action and returns if the action was executed or not, if the switch is IMMEDIATELY on (i.e. no lock involved)
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>Return <c>true</c> if the switch was on</returns>
        public bool IfOn(Action action)
        {
            if(_switch.Value)
            {
                action();
                return true;
            }
            return false;
        }


        /// <summary>
        /// Executes the provided action and returns if the action was executed or not, if the switch is IMMEDIATELY off (i.e. no lock involved)
        /// </summary>
        /// <param name="action">The action.</param>
        /// <returns>Return <c>true</c> if the switch was off</returns>
        public bool IfOff(Action action)
        {
            if(!_switch.Value)
            {
                action();
                return true;
            }
            return false;
        }


        /// <summary>
        /// Executes the provided action and returns if the action was executed or not, if the switch is on, waiting for any pending changes to happen before (locking)
        /// Be careful of longrunning or blocking within the provided action as it can lead to deadlocks or bad performance
        /// </summary>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public bool WhileOn(Action action)
        {
            lock(_lock)
            {
                if(_switch.Value)
                {
                    action();
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Executes the provided action and returns if the action was executed or not, if the switch is off, waiting for any pending changes to happen before (locking)
        /// Be careful of longrunning or blocking within the provided action as it can lead to deadlocks or bad performance
        /// </summary>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public bool WhileOff(Action action)
        {
            lock(_lock)
            {
                if(!_switch.Value)
                {
                    action();
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this switch is on. No locking.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is on; otherwise, <c>false</c>.
        /// </value>
        public bool IsOn
        {
            get { return _switch.Value; }
        }

        /// <summary>
        /// Gets a value indicating whether this switch is off. No locking.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is off; otherwise, <c>false</c>.
        /// </value>
        public bool IsOff
        {
            get { return !_switch.Value; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        public void Locked(Action action)
        {
            lock (_lock)
            {
                action();
            }
        }
    }
}

