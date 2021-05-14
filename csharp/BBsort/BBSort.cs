﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flexols.Data.Collections;


namespace BBsort.Counting
{
    public static class LogHelper
    {

        [StructLayout(LayoutKind.Explicit)]
        private struct ConverterStruct
        {
            [FieldOffset(0)] public int x;
            [FieldOffset(0)] public float val;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float fastLog2(float val)
        {
            ConverterStruct u;
            u.x = 0; u.val = val;

            float lg2 = (float)(((u.x >> 23) & 255) - 128);
            u.x &= ~(255 << 23);
            u.x += 127 << 23;
            return lg2 + ((-0.3358287811f) * u.val + 2.0f) * u.val - 0.65871759316667f;
        }
    }


    public class BBSort<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        public static float getLog(float x)
        {

            var abs = Math.Abs(x);
            if (abs < 2)
            {
                return x;
            }
            var lg = LogHelper.fastLog2(abs);
            return (float)(x < 0 ? -lg : lg);
        }
        
        public static float getLog(int x)
        {

            var abs = Math.Abs(x);
            if (abs < 2)
            {
                return x;
            }
            var lg = LogHelper.fastLog2(abs);
            return (float)(x < 0 ? -lg : lg);
        }

        Func<T, float> m_getLog;

        public BBSort(Func<T, float> getLog)
        {
            m_getLog = getLog;
        }

        public void Sort(List<T> array)
        {

            if (array.Count <= 1)
            {
                return;
            }

            var st = new Stack<MinMaxHeap<T>>();
            var countMap = new Dictionary<T, int>();

            getTopStackBuckets(array, st, countMap);

            bbSortToStream(st, array, array.Count, countMap);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        (float, float) GetLinearTransformParams(float x1, float x2, float y1, float y2)
        {
            float dx = x1 - x2;

            if (dx != 0.0)
            {
                float a = (y1 - y2) / dx;
                float b = y1 - (a * x1);

                return (a, b);
            }

            return (0.0f, 0.0f);
        }

        void getBuckets(ref T min, ref T max, MinMaxHeap<T> iterable, PoolList<MinMaxHeap<T>> buckets, int count)
        {
            var minLog = m_getLog(min);
            var maxLog = m_getLog(max);

            var (a, b) = GetLinearTransformParams(minLog, maxLog, 0, count - 1);

            var cnt = iterable.Count;

            for(int i = 0; i < cnt; i++)
            {
                // ApplyLinearTransform
                int index = (int)((a * m_getLog(iterable.At(i)) + b));
                index = Math.Min(count - 1, index);
                var bucket = buckets[index];

                if (bucket == null)
                {
                    const int maxCapacity = int.MaxValue;
                    const int arraySize = 2;
                    
                    buckets[index] = bucket = new MinMaxHeap<T>(new PoolList<T>(maxCapacity, arraySize));
                }
                bucket.Add(iterable.At(i));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        void fillStream(ref T val, List<T> output, int index, int count)
        {
            for (int i = 0; i < count; ++i)
            {

                int newIndex = index + i;
                if (newIndex >= output.Count)
                {
                    break;
                }
                output[newIndex] = val;
            }
        }

        int case1(Stack<MinMaxHeap<T>> st,
                  MinMaxHeap<T> top,
                  List<T> output,
                  Dictionary<T, int> countMap,
                  int index)
        {
            fillStream(ref top.At(0), output, index, countMap[top.At(0)]);

            return top.Count;
        }

        int case2(Stack<MinMaxHeap<T>> st,
                      MinMaxHeap<T> top,
                      List<T> output,
                      Dictionary<T, int> countMap,
                      int index)
        {
            var count0 = countMap[top.At(1)];
            var count1 = countMap[top.At(0)];

            fillStream(ref top.At(1), output, index, count0);
            fillStream(ref top.At(0), output, index + count0, count1);

            return count0 + count1;
        }

        int case3(Stack<MinMaxHeap<T>> st,
                      MinMaxHeap<T> top,
                      List<T> output,
                      Dictionary<T, int> countMap,
                      int index)
        {

            //single comparison
            var (maxIndex, midIndex, minIndex) = top.GetMaxMidMin();

            var count1 = countMap[top.At(minIndex)];
            var count2 = countMap[top.At(midIndex)];
            var count3 = countMap[top.At(maxIndex)];

            fillStream(ref top.At(minIndex), output, index, count1);
            fillStream(ref top.At(midIndex), output, index + count1, count2);
            fillStream(ref top.At(maxIndex), output, index + count1 + count2, count3);

            var count = count1 + count2 + count3;

            return count;
        }

        int caseN(Stack<MinMaxHeap<T>> st,
                      MinMaxHeap<T> top,
                      List<T> output,
                      Dictionary<T, int> countMap,
                      int index)
        {

            var count = (top.Count / 2) + 1;

            var newBuckets = new PoolList<MinMaxHeap<T>>(count, count, count);

            getBuckets(ref top.FindMin(), ref top.FindMax(), top, newBuckets, count);

            for (int i = newBuckets.Count - 1; i >= 0; --i)
            {
                var minMaxHeap = newBuckets[i];
                
                if (minMaxHeap != null)
                {
                    st.Push(minMaxHeap);
                }
            }
            return 0;
        }

        void bbSortToStream(Stack<MinMaxHeap<T>> st, List<T> output, int count, Dictionary<T, int> countMap)
        {
            var caseFunc = new Func<Stack<MinMaxHeap<T>>, MinMaxHeap<T>, List<T>, Dictionary<T, int>, int, int>[] { case1, case2, case3, caseN };

            int index = 0;

            while (st.Count > 0 && index < count)
            {
                var top = st.Pop();
                var caseIndex = Math.Min(top.Count - 1, 3);

                index += caseFunc[caseIndex].Invoke(st, top, output, countMap, index);
            }
        }

        void getTopStackBuckets(List<T> array,
                                Stack<MinMaxHeap<T>> st,
                                Dictionary<T, int> countMap)
        {

            MinMaxHeap<T> distinctItems = new MinMaxHeap<T>(new PoolList<T>(array.Count, 4));

            for (var index = 0; index < array.Count; index++)
            {
                if (countMap.ContainsKey(array[index]))
                {
                    countMap[array[index]] += 1;
                }
                else
                {
                    distinctItems.Add(array[index]);
                    countMap[array[index]] = 1;
                }
            }

            caseN(st, distinctItems, array, countMap, 0);          
        }
    }
}
