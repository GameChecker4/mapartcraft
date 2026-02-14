using System;
using System.Collections.Generic;
using System.Linq;

namespace MapartSolving
{
    public class SharedArray<T>
    {
        private readonly int[] _pointers;
        public readonly T[] Values;

        public static SharedArray<T> Create(IEnumerable<int> pointers, IEnumerable<T> values)
        {
            var valuesArray = values.ToArray();
            var pointersArray = pointers.Append(valuesArray.Length).ToArray();
            Debug.Assert(pointersArray[0] == 0);
            for (int i = 1; i < pointersArray.Length; i++)
                Debug.Assert(pointersArray[i] >= pointersArray[i - 1]);
        
            return new SharedArray<T>(pointersArray, valuesArray);
        }
    
        private SharedArray(int[] pointers, T[] values)
        {
            _pointers = pointers;
            Values = values;
        }

        private SharedArray(int[] pointers, int arrayLength)
        {
            _pointers = pointers;
            Values = new T[arrayLength];
        }

        public Span<T> this[int index] => GetArray(index);

        public Span<T> GetArray(int index)
        {
            int start = GetPointer(index, out int count);
            return Values.AsSpan(start, count);
        }
        
        public int ArraysCount => _pointers.Length - 1;

        public int GetPointer(int arrayIndex, out int count)
        {
            int pointer = _pointers[arrayIndex];
            int next = _pointers[arrayIndex + 1];
            count = next - pointer;
            return pointer;
        }

        public int GetArrayIndexByPointer(int pointer)
        {
            Debug.Assert(pointer >= 0 && pointer < Values.Length);
            int index = Array.BinarySearch(_pointers, pointer);
            if (index < 0)
                index = ~index - 1;
            return index;
        }

        public SharedArray<TOut> ClonePointers<TOut>() => new(_pointers, Values.Length);
    
        public delegate TOut ValueSelector<out TOut>(T value);
        public SharedArray<TOut> ClonePointers<TOut>(ValueSelector<TOut> valueSelector) => new(_pointers, Values.Select(valueSelector.Invoke).ToArray());
    }
}