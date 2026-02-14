using System.Threading;

namespace MapartSolving
{
    public class ConcurrentIncrementer
    {
        private volatile int _currentValue;

        public int MaxValue { get; private set; }

        public bool IsEnded => _currentValue > MaxValue;

        public void Reset(int maxValue)
        {
            _currentValue = -1;
            MaxValue = maxValue;
        }

        public bool TryGetNext(out int index)
        {
            if (_currentValue > MaxValue)
            {
                index = -1;
                return false;
            }

            index = Interlocked.Increment(ref _currentValue);
            if (index <= MaxValue)
                return true;

            index = -1;
            return false;
        }
    }
}