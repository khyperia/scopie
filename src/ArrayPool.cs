using System.Collections.Generic;

namespace Scopie
{
    public static class ArrayPool<T>
    {
        private static object _lock = new object();
        private const int _capacity = 16;
        public static List<T[]> _pool = new List<T[]>();

        public static void Free(T[] array)
        {
            lock (_lock)
            {
                _pool.Add(array);
                if (_pool.Count > _capacity)
                {
                    _pool.RemoveAt(0);
                }
            }
        }

        public static T[] Alloc(int length)
        {
            lock (_lock)
            {
                for (var i = _pool.Count - 1; i >= 0; i--)
                {
                    if (_pool[i].Length == length)
                    {
                        var result = _pool[i];
                        _pool.RemoveAt(i);
                        return result;
                    }
                }
                return new T[length];
            }
        }
    }
}
