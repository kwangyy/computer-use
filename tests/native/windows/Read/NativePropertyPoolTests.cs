// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ComputerUse.Windows
{
    internal static class NativePropertyPoolTests
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        static void RoundTrip()
        {
            var nodes = Enumerable.Range(0, 120).Select(i => Wire.Obj("id", "node:" + i,
                "properties", new object[] {
                    NativeObservationGraph.Property("Name", i % 2 == 0 ? "first label" : "second label", true),
                    NativeObservationGraph.Property("UIA.Collection", Wire.Obj("visible", true, "count", i % 3), true),
                    NativeObservationGraph.Property("Text.DocumentRange", null, false,
                        Wire.Obj("state", "timeout", "detail", "provider budget expired")),
                    NativeObservationGraph.Property("RawViewParent", new NativeObservationReference("node:0"), true)
                })).ToArray();
            var before = Wire.Json(nodes);
            var graph = Wire.Obj("nodes", nodes);
            NativeObservationGraph.CompactProperties(graph, nodes);
            var pool = Wire.Array(graph["propertyPool"]);
            Require(pool.Length == 6, "Different scalar/nested values must keep distinct pool entries");
            int compact = Encoding.UTF8.GetByteCount(Wire.Json(graph));
            Require(compact < Encoding.UTF8.GetByteCount(before) / 2,
                    "Repeated ordinary values/status records must shrink, not only unsupported keys");
            foreach (var node in nodes)
            {
                var properties = Wire.Array(node["properties"]);
                Require(!Wire.Object(properties[3]).ContainsKey("property"), "Native parent references must stay inline");
                node["properties"] = properties.Select(p => (object)NativeObservationGraph.PropertyRecord(
                    p, pool)).ToArray();
            }
            Require(Wire.Json(nodes) == before, "Every property, native value and error must round-trip in place");
            var query = Wire.Obj("kind", "nodes", "fields", new[] { "Name" }, "maxNodes", 1, "maxDepth", 0,
                                 "poolProperties", true);
            NativeQuerySchema.Validate(query);
            query["poolProperties"] = 1;
            bool rejected = false;
            try { NativeQuerySchema.Validate(query); } catch (Failure) { rejected = true; }
            Require(rejected, "The negotiated pool option remains an exact boolean");
            Console.WriteLine("Windows exact property pool: " + Encoding.UTF8.GetByteCount(before) + " -> " + compact);
        }

        static int Main(string[] args)
        {
            try
            {
                RoundTrip();
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
