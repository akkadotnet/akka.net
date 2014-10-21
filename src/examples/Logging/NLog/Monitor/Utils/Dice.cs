using System;
using System.Collections.Generic;

namespace Monitor.Utils
{
    /// <summary>
    /// Very simple D6 dice roller
    /// </summary>
    public class Dice : IDisposable
    {
        private Action _defaultAction;
        private readonly Dictionary<int, Action> _diceRollActions = new Dictionary<int, Action>();

        public Dice(Action defaultAction)
        {
            _defaultAction = defaultAction;
        }

        public Dice On(int result, Action action)
        {
            _diceRollActions[result] = action;
            return this;
        }

        public void Roll()
        {
            var rnd = new Random(DateTime.UtcNow.Millisecond);
            var result = rnd.Next(1, 7);    // random number from <1,7) range

            if (_diceRollActions.ContainsKey(result))
            {
                _diceRollActions[result]();
            }
            else
            {
                _defaultAction();
            }
        }

        public void Dispose()
        {
            _diceRollActions.Clear();
            _defaultAction = null;
            GC.SuppressFinalize(this);
        }
    }
}