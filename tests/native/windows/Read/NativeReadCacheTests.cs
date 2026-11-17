// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Windows.Automation;

namespace ComputerUse.Windows
{
    internal static class NativeReadCacheTests
    {
        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static void ChildQueryRoutes()
        {
            int walks = 0, snapshots = 0;
            Action<Exception> unexpected = error => { throw new InvalidOperationException("Unexpected native failure", error); };
            var leaf = NativeReadChildRoutes.Inspect<object>(
                () => { walks++; return null; },
                () => { snapshots++; return new object[0]; }, unexpected, unexpected);
            Require(leaf.First == null && !leaf.UseSnapshot && !leaf.ConflictingEmptyCache &&
                    walks == 1 && snapshots == 0,
                    "a confirmed Raw View leaf must not rebuild a full property cache just to confirm no children");
            walks = snapshots = 0;
            var child = new object();
            var siblings = new[] { child, new object() };
            var branch = NativeReadChildRoutes.Inspect(
                () => { walks++; return child; },
                () => { snapshots++; return siblings; }, unexpected, unexpected);
            Require(branch.UseSnapshot && Object.ReferenceEquals(branch.Cached, siblings) &&
                    walks == 1 && snapshots == 1,
                    "non-leaf children retain one batched property snapshot rather than per-child cache queries");
            walks = 0;
            var conflict = NativeReadChildRoutes.Inspect(
                () => { walks++; return child; }, () => new object[0], unexpected, unexpected);
            Require(!conflict.UseSnapshot && conflict.ConflictingEmptyCache && walks == 2,
                    "an empty cache after a positive first-child observation must retain its fresh walker disagreement");
            int failures = 0;
            var recovered = NativeReadChildRoutes.Inspect<object>(
                () => { throw new InvalidOperationException("walker unavailable"); },
                () => siblings, error => failures++, unexpected);
            Require(recovered.UseSnapshot && failures == 1,
                    "an unavailable walker probe must not disable a healthy cached children route");
        }

        static int Main()
        {
            try
            {
                ChildQueryRoutes();
                var requested = NativeReadCache.RequestedProperties(new[] {
                    "Name", "LabeledBy", "DescribedBy", "FlowsTo", "FlowsFrom",
                    "Selection.Selection", "Table.RowHeaders", "RawViewChildren"
                });
                Require(requested.Contains(AutomationElement.NameProperty.Id) &&
                        requested.Contains(AutomationElement.LabeledByProperty.Id) &&
                        requested.Contains(30105) && requested.Contains(30106) && requested.Contains(30148) &&
                        requested.Contains(SelectionPattern.SelectionProperty.Id) &&
                        requested.Contains(TablePattern.RowHeadersProperty.Id),
                        "requested element-valued relations must join the same property cache as ordinary fields");
                Require(!requested.Contains(TablePattern.ColumnHeadersProperty.Id),
                        "the observation must not fetch unrequested table relations");

                // The cached native reference is delivered intact; input
                // identity/scope validation remains the collector's work.
                object relation = new object();
                int current = 0, failures = 0;
                var actual = NativeCachedPropertyGate.Read(() => relation,
                    () => { current++; return new object(); }, error => failures++, () => {});
                Require(Object.ReferenceEquals(actual, relation) && current == 0 && failures == 0,
                        "a cached relation must not be replaced by a second current-property sample");
                var original = new InvalidOperationException("cached relation decode failed");
                Exception captured = null;
                actual = NativeCachedPropertyGate.Read(() => { throw original; },
                    () => { current++; return relation; }, error => captured = error, () => {});
                Require(Object.ReferenceEquals(actual, relation) && current == 1 &&
                        Object.ReferenceEquals(captured, original),
                        "an unavailable cached relation retains failed evidence and uses the existing current fallback once");
                Console.WriteLine("Windows read cache tests passed (relation batching, native reference fidelity, failure fallback)");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }
}
