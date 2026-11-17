// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Automation;

namespace ComputerUse.Windows
{
    internal static class NativeObservationWireBudgetTests
    {
        static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        static Dictionary<string, object> Observe(int count)
        {
            var rows = new List<object>();
            var edges = new List<object>();
            var fields = new[] { "Name" }.Concat(Enumerable.Range(0, 72).Select(i => "Provider.Field" + i)).ToArray();
            for (int index = 0; index < count; index++)
            {
                string id = "node:" + index;
                var statuses = fields.Skip(1).ToDictionary(field => field, field => (object)Wire.Obj(
                    "state", "timeout", "detail", "Provider transaction reached its deadline",
                    "nativeError", Wire.Obj("type", "COMException", "hresult", unchecked((int)0x8001011f),
                                            "message", new String('x', 420) + ":" + index + ":" + field)));
                rows.Add(Wire.Obj("node", id, "requestedFields", fields, "values", Wire.Obj("Name", "Visible " + id),
                                  "fieldStatus", statuses, "actions", Wire.Obj("state", "complete", "items", new object[0])));
                edges.Add(Wire.Obj("source", id, "property", "RawViewChildren", "targets",
                                   index + 1 < count ? new[] { "node:" + (index + 1) } : new string[0],
                                   "state", "complete", "certainty", "confirmed"));
            }
            return NativeObservationGraph.Encode("session", "surface", Wire.Stamp(Wire.Now), new[] { "node:0" },
                                                 rows, edges, new string[0], new string[0], new[] { "RawViewChildren" },
                                                 poolReadMetadata: true).Graph;
        }

        static void TraversalCoverage(bool expandChild)
        {
            var notes = new List<string>();
            var traversal = new NativeObservationTraversal(2000, 16);
            Require(traversal.MaxNodes == 1024, "the relation count must still bound the collection ceiling");
            var rows = new List<object>();
            var edges = new List<object>();
            var seen = new HashSet<string>();
            Action<string, string, string[]> edge = (id, property, targets) => edges.Add(Wire.Obj(
                "source", id, "property", property, "targets", targets, "state", "complete", "certainty", "confirmed"));
            Action<string, string[], string[]> node = (id, children, parent) =>
            {
                seen.Add(id);
                rows.Add(Wire.Obj("node", id, "requestedFields", new[] { "Name" },
                                  "values", Wire.Obj("Name", id), "fieldStatus", Wire.Obj(),
                                  "actions", Wire.Obj("state", "complete", "items", new object[0])));
                edge(id, "RawViewChildren", children);
                edge(id, "RawViewParent", parent);
            };
            node("root", new[] { "child" }, new[] { "outside" });
            traversal.Reference("outside", false);
            traversal.Reference("child", true);
            if (expandChild)
                node("child", new string[0], new[] { "root" });
            var unread = traversal.Unread(seen, notes);
            var graph = NativeObservationGraph.Encode("session", "surface", Wire.Stamp(Wire.Now), new[] { "root" },
                rows, edges, unread, notes, new[] { "RawViewChildren", "RawViewParent" }).Graph;
            var read = Wire.Object(graph["read"]);
            var declared = Wire.Array(graph["nodes"]).Select(Wire.Object).ToDictionary(row => (string)row["id"]);
            Require(declared["outside"]["properties"] == null,
                    "the exact out-of-scope parent must remain declared without an invented body");
            Require(Equals(read["complete"], expandChild) && Wire.Array(read["issues"]).Length == (expandChild ? 0 : 1),
                    "only omitted requested traversal may mark this tiny graph partial");
            Require(unread.SequenceEqual(expandChild ? new string[0] : new[] { "child" }) &&
                    (declared["child"]["properties"] != null) == expandChild,
                    "requested omissions must remain declared and distinguishable from external targets");
        }

        static void TraversalNodeLimit()
        {
            var notes = new List<string>();
            var traversal = new NativeObservationTraversal(2000, 16);
            traversal.Reference("remaining", true);
            var seen = new HashSet<string>(Enumerable.Range(0, traversal.MaxNodes).Select(i => "node:" + i));
            Require(traversal.ChildPageLimit(1, 8000) == traversal.MaxNodes - 1 &&
                    traversal.ChildPageLimit(1, 8000) >= 181,
                    "ordinary list membership must use the caller's available traversal budget, not stop at 128");
            Require(traversal.ChildPageLimit(1, 12) == 12 &&
                    traversal.ChildPageLimit(traversal.MaxNodes, 8000) == 128,
                    "edge capacity and existing declaration-only page behavior must remain bounded");
            Require(traversal.Unread(seen, notes).SequenceEqual(new[] { "remaining" }) && notes.Any(note =>
                        note.Contains("relation-status node limit")),
                    "hitting the actual lowered ceiling with queued work must report the node limit");
        }

        static void StrictWindowBodies()
        {
            var outside = new NativeNodeRecord(new NativeObservationScope(NativeOwnershipState.Outside, "other HWND"),
                                               null, false);
            var virtualChild = new NativeNodeRecord(NativeObservationScope.DeferredRelation, null, false);
            Require(!NativeObservationTraversal.IncludesBody(outside, true),
                    "a strict window must never read a confirmed outside node body");
            Require(NativeObservationTraversal.IncludesBody(outside, false),
                    "an ordinary observation retains its diagnostic outside-node behavior");
            Require(NativeObservationTraversal.IncludesBody(virtualChild, true),
                    "an HWND-less virtual descendant is not evidence of another window");
            var root = Wire.Obj("node", "root", "requestedFields", new[] { "Name" },
                "values", Wire.Obj("Name", "Selected"), "fieldStatus", Wire.Obj(),
                "actions", Wire.Obj("state", "complete", "items", new object[0]));
            var relation = Wire.Obj("source", "root", "property", "RawViewChildren", "targets", new[] { "other" },
                                    "state", "complete", "certainty", "confirmed");
            var graph = NativeObservationGraph.Encode("session", "surface", Wire.Stamp(Wire.Now), new[] { "root" },
                new[] { root }, new[] { relation }, new string[0], new string[0], new[] { "RawViewChildren" },
                outside: new[] { "other" }).Graph;
            var declaration = Wire.Array(graph["nodes"]).Select(Wire.Object).Single(row => Equals(row["id"], "other"));
            Require(declaration["properties"] == null && Wire.Array(Wire.Object(declaration["read"])["issues"]).Any(
                        issue => Equals(Wire.Object(issue)["reason"], "outside_window_scope")),
                    "retain a typed outside-window declaration without its properties");
            var original = Wire.Array(graph["nodes"]).Select(Wire.Object).Single(row => Equals(row["id"], "root"));
            var retained = Wire.Array(original["properties"]).Select(Wire.Object).Single(property =>
                Equals(Wire.Array(property["key"])[1], "RawViewChildren"));
            Require(Equals(Wire.Object(retained["read"])["complete"], true),
                    "outside-node exclusion must not invent a gap in the completed relation");
        }

        static void PopupChildBoundary()
        {
            var list = new NativeNodeRecord(NativeObservationScope.DeferredRelation, "list-runtime", false);
            var popup = new NativeNodeRecord(new NativeObservationScope(NativeOwnershipState.Outside,
                "Raw View parent belongs to the independent popup."), "popup-runtime", false);
            list.ObserveParentBoundary(popup);
            Require(!NativeObservationTraversal.IncludesBody(list, true),
                    "a combo's HWND-less child list with a proved outside parent must not publish input rows under the form");
            Require(NativeObservationTraversal.IncludesBody(list, false),
                    "an explicit diagnostic traversal still retains foreign relationship facts");
            var unknown = new NativeNodeRecord(NativeObservationScope.DeferredRelation, "unknown", false);
            var virtualChild = new NativeNodeRecord(NativeObservationScope.DeferredRelation, "virtual", false);
            virtualChild.ObserveParentBoundary(unknown);
            Require(NativeObservationTraversal.IncludesBody(virtualChild, true),
                    "missing parent HWND evidence must not exclude a legitimate virtual child");
            var hosted = new NativeNodeRecord(new NativeObservationScope(NativeOwnershipState.Confirmed,
                "Native HWND belongs to the selected window."), "hosted", false);
            hosted.ObserveParentBoundary(popup);
            Require(NativeObservationTraversal.IncludesBody(hosted, true),
                    "a confirmed hosted window must retain its own positive containment evidence");
        }

        static void StrictWindowRootCandidates()
        {
            var peer = new NativeWindowRecord { Native = new Window { Ref = "blocking-provider" } };
            var selected = new NativeWindowRecord { Native = new Window { Ref = "selected" } };
            var catalogue = new[] { peer, selected };
            Require(NativeRootProbe.Candidates(catalogue, "selected").SequenceEqual(new[] { selected }),
                    "a strict window read must not start the sibling root provider");
            Require(NativeRootProbe.Candidates(catalogue, null).SequenceEqual(catalogue),
                    "the ordinary root probe must retain full catalogue behavior");
        }

        static void OwnedMenuContent()
        {
            Require(NativeRootProbe.MenuContent(ControlType.Window, "owner") == null &&
                    NativeRootProbe.MenuContent(null, "owner") == null,
                    "ownership alone cannot classify a popup as a menu");
            var menu = NativeRootProbe.MenuContent(ControlType.Menu, "owner");
            Require(Equals(menu["kind"], "menu") && Equals(menu["host"], "owner") &&
                    Equals(menu["evidence"], "UIA.ControlType.Menu") && !menu.ContainsKey("hostNode"),
                    "a proved menu retains its separate native surface under its confirmed owner");
            Require(NativeRootProbe.MenuContent(ControlType.Menu, null)["host"] == null,
                    "menu role evidence cannot invent an owner");
        }

        // A normal UIA list has many children and many explicitly unsupported
        // optional fields. Diagnostic repetition must not consume their bodies.
        static void OrdinaryListCoverage()
        {
            var rows = new List<object>();
            var edges = new List<object>();
            var fields = new[] { "Name", "ControlType", "BoundingRectangle", "IsEnabled" }
                .Concat(Enumerable.Range(0, 60).Select(i => "OptionalPattern.Field" + i)).ToArray();
            var optional = fields.Skip(4).ToDictionary(field => field, field => (object)Wire.Obj(
                "state", "unsupported", "detail", "UIA does not expose the required pattern"));
            for (int i = 0; i < 220; i++)
            {
                string id = "item:" + i;
                rows.Add(Wire.Obj("node", id, "requestedFields", fields,
                    "values", Wire.Obj("Name", "Option " + i, "ControlType", "ControlType.ListItem",
                        "BoundingRectangle", Wire.Obj("x", 10, "y", i * 30, "width", 200, "height", 30),
                        "IsEnabled", true),
                    "fieldStatus", optional,
                    "actions", Wire.Obj("state", "complete", "items", new object[0])));
                edges.Add(Wire.Obj("source", id, "property", "RawViewChildren", "targets",
                    i + 1 < 220 ? new[] { "item:" + (i + 1) } : new string[0],
                    "state", "complete", "certainty", "confirmed"));
            }
            var graph = NativeObservationGraph.Encode("session", "surface", Wire.Stamp(Wire.Now),
                new[] { "item:0" }, rows, edges, new string[0], new string[0],
                new[] { "RawViewChildren" }).Graph;
            var nodes = Wire.Array(graph["nodes"]).Select(Wire.Object).ToArray();
            Require(Equals(Wire.Object(graph["read"])["complete"], true) &&
                    nodes.Length == 220 && nodes.All(node => node["properties"] != null),
                    "ordinary list bodies were truncated by repeated optional-field diagnostics");
            Require(Wire.Array(graph["metadataValues"]).Length == 1,
                    "one query must share its exact requested-field metadata across all nodes");
            foreach (var node in nodes)
            {
                var properties = Wire.Array(node["properties"]).Select(Wire.Object).ToArray();
                Require(properties.Count(property => Equals(property["state"], "unsupported")) == 60,
                        "known unsupported fields must remain explicit rather than appear unread or supported");
            }
            Console.WriteLine("Ordinary UIA list complete: " + Encoding.UTF8.GetByteCount(Wire.Json(graph)) + " bytes");
        }

        static void UnsupportedEvidence()
        {
            var ordinary = NativeObservationGraph.Property("Value.Value", null, false,
                Wire.Obj("state", "unsupported", "detail", "UIA does not expose the required pattern"));
            Require(Equals(ordinary["state"], "unsupported") && ordinary["value"] == null &&
                    !ordinary.ContainsKey("read"),
                    "ordinary absence keeps its explicit fact without a fabricated failed transaction");
            var realFailure = NativeObservationGraph.Property("Value.Value", null, false,
                Wire.Obj("state", "unsupported", "detail", "UIA does not expose the required pattern",
                    "nativeError", Wire.Obj("type", "COMException", "hresult", -1, "message", "provider failure")));
            var issue = Wire.Object(Wire.Array(Wire.Object(realFailure["read"])["issues"])[0]);
            Require(issue.ContainsKey("nativeStatus"), "a real provider error must retain its original evidence");
            var protectedText = NativeObservationGraph.Property("Text.DocumentRange", null, false,
                Wire.Obj("state", "unsupported", "detail", "protected text is not captured"));
            Require(protectedText.ContainsKey("read"), "distinct absence reasons must remain available");
            var partial = NativeObservationGraph.Property("Name", "prefix", true,
                Wire.Obj("state", "partial", "detail", "provider stopped", "textRef", "native:text"));
            var partialIssue = Wire.Object(Wire.Array(Wire.Object(partial["read"])["issues"])[0]);
            Require(Equals(partial["state"], "value") && partialIssue.ContainsKey("continuation"),
                    "partial acquired values retain both the payload and continuation evidence");
        }

        static void RepeatedReadCoverage()
        {
            const int count = 300;
            var fields = new[] { "Name" }.Concat(Enumerable.Range(0, 75).Select(i => "Optional.Field" + i)).ToArray();
            var rows = new List<object>();
            var edges = new List<object>();
            var relations = Enumerable.Range(0, 16).Select(i => "Optional.Relation" + i).ToArray();
            for (int i = 0; i < count; i++)
            {
                string id = "dense:" + i;
                rows.Add(Wire.Obj("node", id, "requestedFields", fields,
                    "values", Wire.Obj("Name", "Option " + i),
                    "fieldStatus", fields.Skip(1).ToDictionary(field => field, field => (object)Wire.Obj(
                        "state", "unsupported", "detail", "UIA does not expose the required pattern")),
                    "actions", Wire.Obj("state", "complete", "items", new object[0])));
                foreach (string relation in relations)
                    edges.Add(Wire.Obj("source", id, "property", relation, "targets", new string[0],
                        "state", "unsupported", "certainty", "unknown",
                        "detail", "UIA does not expose the required pattern"));
            }
            var graph = NativeObservationGraph.Encode("session", "surface", Wire.Stamp(Wire.Now),
                new[] { "dense:0" }, rows, edges, new string[0], new string[0], relations,
                poolReadMetadata: true).Graph;
            var nodes = Wire.Array(graph["nodes"]).Select(Wire.Object).ToArray();
            Require(Equals(Wire.Object(graph["read"])["complete"], true) &&
                    nodes.Length == count && nodes.All(node => node["properties"] != null),
                    "repeated relation read records must not crowd out ordinary dense-list bodies");
            Require(nodes.All(node => Wire.Array(node["properties"]).Length == fields.Length + relations.Length + 1),
                    "every requested field, relation and action catalogue must survive read-record sharing");
            Console.WriteLine("Repeated relation list complete: " + Encoding.UTF8.GetByteCount(Wire.Json(graph)) + " bytes");
        }

        static object ExpandReads(object value, object[] table)
        {
            var record = value as Dictionary<string, object>;
            if (record != null)
            {
                if (record.Count == 1 && record.ContainsKey("metadataRead"))
                    return ExpandReads(table[(int)record["metadataRead"]], table);
                return record.Where(pair => pair.Key != "readMetadata").ToDictionary(pair => pair.Key,
                    pair => ExpandReads(pair.Value, table));
            }
            if (value is System.Collections.IEnumerable && !(value is string))
                return ((System.Collections.IEnumerable)value).Cast<object>().Select(item => ExpandReads(item, table)).ToArray();
            return value;
        }

        static void ReadPoolFidelity()
        {
            var reads = new List<object>();
            for (int i = 0; i < 6; i++)
            {
                var status = Wire.Obj("state", "error", "detail", "provider error", "textRef", "continue:exact",
                    "nativeError", Wire.Obj("type", "COMException", "hresult", i < 4 ? -1 : -2,
                                           "message", "Exact provider failure"));
                reads.Add(NativeObservationGraph.Property("Text.DocumentRange", "partial text", true, status));
            }
            for (int i = 0; i < 2; i++)
            {
                var property = NativeObservationGraph.Property("relation-evidence", "value", true);
                property["read"] = NativeObservationGraph.Read("provider", false, null, new[] {
                    NativeObservationGraph.Issue("partial", "Exact provider relation", new NativeObservationReference("external")) });
                reads.Add(property);
            }
            var node = Wire.Obj("id", "owner", "properties", reads);
            var graph = Wire.Obj("nodes", new[] { node });
            string before = Wire.Json(graph);
            NativeObservationGraph.CompactReadMetadata(graph, new[] { node });
            var table = Wire.Array(graph["readMetadata"]);
            Require(table.Length == 2 && Wire.Json(ExpandReads(graph, table)) == before,
                    "pool expansion must preserve every native status, partial payload, continuation and distinct error exactly");
            Require(reads.Skip(6).Cast<Dictionary<string, object>>().All(property =>
                    !Wire.Object(property["read"]).ContainsKey("metadataRead")),
                    "native-reference evidence must remain inline for bounded declaration closure");
        }

        static void ReadPoolNegotiation()
        {
            var row = Wire.Obj("node", "root", "requestedFields", new[] { "Name" }, "values", Wire.Obj("Name", "root"),
                "fieldStatus", Wire.Obj(), "actions", Wire.Obj("state", "complete", "items", new object[0]));
            var peer = new Dictionary<string, object>(row);
            peer["node"] = "peer";
            var observed = Wire.Stamp(Wire.Now);
            Func<bool, Dictionary<string, object>> encode = enabled => NativeObservationGraph.Encode(
                "session", "surface", observed, new[] { "root" }, new[] { row, peer }, new object[0],
                new string[0], new string[0], new string[0], poolReadMetadata: enabled).Graph;
            var graph = encode(false);
            Require(!graph.ContainsKey("readMetadata") && !Wire.Json(graph).Contains("\"metadataRead\""),
                    "old callers must retain their compatible inline wire format, even when records repeat");
            Require(encode(true).ContainsKey("readMetadata"), "explicit opt-in must enable sharing of the same repeated records");
            var query = Wire.Obj("kind", "nodes", "fields", new[] { "Name" }, "maxNodes", 1, "maxDepth", 0,
                                 "poolReadMetadata", true);
            NativeQuerySchema.Validate(query);
            query["poolReadMetadata"] = "true";
            bool rejected = false;
            try { NativeQuerySchema.Validate(query); }
            catch (Failure) { rejected = true; }
            Require(rejected, "read pooling opt-in must be an exact boolean");
        }

        static int Main(string[] args)
        {
            try
            {
                UnsupportedEvidence();
                ReadPoolFidelity();
                ReadPoolNegotiation();
                RepeatedReadCoverage();
                OrdinaryListCoverage();
                TraversalCoverage(true);
                TraversalCoverage(false);
                TraversalNodeLimit();
                StrictWindowBodies();
                PopupChildBoundary();
                StrictWindowRootCandidates();
                OwnedMenuContent();
                var small = Observe(2);
                Require(Equals(Wire.Object(small["read"])["complete"], true), "small reads must stay complete");
                var graph = Observe(150);
                int bytes = Encoding.UTF8.GetByteCount(Wire.Json(graph));
                Require(bytes <= 4 * 1024 * 1024, "typed observation exceeded the response budget: " + bytes);
                var read = Wire.Object(graph["read"]);
                Require(Equals(read["complete"], false) && Wire.Array(read["issues"]).Any(
                            raw => Equals(Wire.Object(raw)["reason"], "observation_wire_budget_exhausted")),
                        "the delivered read must account for omitted native facts");
                var nodes = Wire.Array(graph["nodes"]).Select(Wire.Object).ToDictionary(row => (string)row["id"]);
                Require(nodes["node:0"]["properties"] != null && nodes.Count < 150,
                        "the root's measured properties must survive the cutoff");
                var seen = nodes.Values.Where(row => row["properties"] != null).ToArray();
                foreach (var row in seen)
                {
                    var relation = Wire.Array(row["properties"]).Select(Wire.Object).Single(property =>
                        Equals(Wire.Array(property["key"])[1], "RawViewChildren"));
                    var items = Wire.Array(Wire.Object(Wire.Object(relation["value"])["data"])["items"]);
                    Require(items.All(item => nodes.ContainsKey((string)Wire.Object(Wire.Object(item)["data"])["ref"])),
                            "all retained relationship targets must be declared");
                }
                if (args.Length == 1)
                    File.WriteAllText(args[0], Wire.Json(graph));
                Console.WriteLine("Windows typed observation wire budget passed: " + bytes + " bytes");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
                return 1;
            }
        }
    }
}
