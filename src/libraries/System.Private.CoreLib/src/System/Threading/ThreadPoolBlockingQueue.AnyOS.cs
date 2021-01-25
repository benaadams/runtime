// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using Internal.Runtime.CompilerServices;

namespace System.Threading
{
    public static partial class ThreadPool
    {
        internal static void RecordBlockingCallsite()
            => ThreadPoolBlockingQueue.Enable();
    }

    internal class ThreadPoolBlockingQueue : IThreadPoolWorkItem
    {
        [ThreadStatic]
        private static object? t_currentWorkItem;

        private static ThreadPoolBlockingQueue? s_processor;
        private static HashSet<object>? s_blockingItems;
        private static bool s_enabled;

        public static bool IsEnabled => s_enabled;

        private readonly ConcurrentQueue<object> _workItems = new();
        private int _processingThreads;

        private ThreadPoolBlockingQueue() { }

        private static ThreadPoolBlockingQueue Processor
        {
            get
            {
                return s_processor ??
                    Interlocked.CompareExchange(ref s_processor, new ThreadPoolBlockingQueue(), null) ??
                    s_processor;
            }
        }

        public static object Enqueue(object workItem)
        {
            ThreadPoolBlockingQueue processor = Processor;
            processor._workItems.Enqueue(workItem);
            return processor;
        }

        void IThreadPoolWorkItem.Execute()
        {
            if (!IsRequired())
            {
                // Max queues already executing.
                return;
            }

            Thread? currentThread = Thread.CurrentThread;
            while (true)
            {
                ConcurrentQueue<object> workItems = _workItems;
                while (workItems.TryDequeue(out object? workItem))
                {
                    // We found work, there may be more work, tell the ThreadPool that we are executing.
                    // Note that this will only ask for a max of #procs threads, so it's safe to call it for every dequeue.
                    ThreadPool.s_workQueue.EnsureThreadRequested();

                    if (ThreadPool.EnableWorkerTracking)
                    {
                        ThreadPoolWorkQueue.DispatchWorkItemWithWorkerTracking(workItem, currentThread);
                    }
                    else if (workItem is Task task)
                    {
                        task.ExecuteFromThreadPool(currentThread);
                    }
                    else
                    {
                        Debug.Assert(workItem is IThreadPoolWorkItem);
                        Unsafe.As<IThreadPoolWorkItem>(workItem).Execute();
                    }

                    ExecutionContext.ResetThreadPoolThread(currentThread);
                    currentThread.ResetThreadPoolThread();
                }

                MarkCompleted();

                if (workItems.IsEmpty || !IsRequired())
                {
                    // Nothing to do or  max queues already executing.
                    break;
                }
            }
        }

        private bool IsRequired()
        {
            ThreadPool.GetMinThreads(out int min, out _);
            int maxParallelism = Math.Max(1, min - 1);
            // Consume at most half of the ThreadCount increases;
            // this allows HillClimbing to unblock the ThreadPool
            maxParallelism = Math.Max(maxParallelism, ThreadPool.ThreadCount / 2);

            int count = Volatile.Read(ref _processingThreads);
            while (count < maxParallelism)
            {
                int prevCount = Interlocked.CompareExchange(ref _processingThreads, count + 1, count);
                if (prevCount == count)
                {
                    return true;
                }
                count = prevCount;
            }

            return false;
        }

        private void MarkCompleted()
        {
            int count = Volatile.Read(ref _processingThreads);

            Debug.Assert(count > 0);
            do
            {
                int prevCount = Interlocked.CompareExchange(ref _processingThreads, count - 1, count);
                if (prevCount == count)
                {
                    break;
                }
                count = prevCount;
            } while (count > 0);
        }

        public static bool RequiresMitigation(object? workItem)
        {
            object? entryPoint = GetEntryPoint(workItem);
            if (entryPoint == null)
            {
                return false;
            }

            return s_blockingItems?.Contains(entryPoint) ?? false;
        }

        public static void RegisterForBlockingDetection(object? workItem)
            => t_currentWorkItem = workItem;

        public static void ClearRegistration()
            => t_currentWorkItem = null;

        public static void Enable()
        {
            if (!s_enabled)
            {
                Volatile.Write(ref s_enabled, true);
            }

            object? workItem = t_currentWorkItem;
            if (workItem is null) return;

            object? entryPoint = GetEntryPoint(workItem);
            if (entryPoint == null)
            {
                return;
            }

            HashSet<object>? items = s_blockingItems;
            if (items?.Contains(entryPoint) ?? false)
            {
                // Already there
                return;
            }

            // Lock as may be a batch of same items and we don't want to
            // create a large amount of HashSet copies for the same item.
            lock (Processor)
            {
                if (items?.Contains(entryPoint) ?? false)
                {
                    // Already there
                    return;
                }

                // Copy on add; so can be used without locks when checking for existance
                items = items is null ? new HashSet<object>() : new HashSet<object>(items);
                items.Add(entryPoint);

                s_blockingItems = items;
            }
        }

        private static object? GetEntryPoint(object? workItem)
        {
            if (workItem is null || ReferenceEquals(workItem, s_processor))
            {
                // Don't mark self
                return null;
            }

            return workItem switch
            {
                QueueUserWorkItemCallbackBase quwi => quwi._delegate.GetMethodImpl(),
                 IAsyncStateMachineBox sm => sm.GetType(),
                 Task t => t.m_action?.GetMethodImpl(),
                 _ =>  workItem?.GetType()
            };
        }
    }
}
