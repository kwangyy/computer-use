// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;

namespace ComputerUse.Windows
{
    internal sealed class NativeReadChildRoute<T>
        where T : class
    {
        internal readonly bool UseSnapshot, ConflictingEmptyCache;
        internal readonly T First;
        internal readonly T[] Cached;
        internal NativeReadChildRoute(bool snapshot, T first = null, bool conflict = false, T[] cached = null)
        {
            UseSnapshot = snapshot;
            First = first;
            ConflictingEmptyCache = conflict;
            Cached = cached;
        }
    }
    internal static class NativeReadChildRoutes
    {
        internal static NativeReadChildRoute<T> Inspect<T>(Func<T> firstChild, Func<T[]> cachedChildren,
                                                          Action<Exception> failedFirst, Action<Exception> failedCache)
            where T : class
        {
            // Most controls are leaves. A successful Raw View query that has
            // no first child is already a complete empty relation; building a
            // full child-property cache and then asking this same question
            // again adds no evidence. Non-leaves still use one batched cache.
            try
            {
                if (firstChild() == null)
                    return new NativeReadChildRoute<T>(false);
            }
            catch (Exception error) { failedFirst(error); }
            T[] cached = null;
            bool cacheSucceeded = false;
            try
            {
                cached = cachedChildren();
                cacheSucceeded = true;
            }
            catch (Exception error) { failedCache(error); }
            if (cacheSucceeded && cached != null && cached.Length != 0)
                return new NativeReadChildRoute<T>(true, cached: cached);
            T first = firstChild();
            return new NativeReadChildRoute<T>(false, first, cacheSucceeded && first != null);
        }
        internal static bool Complete(bool collected, bool conflictingEmptyCache)
        {
            return collected && !conflictingEmptyCache;
        }
    }
}
