// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.
using System;
using System.Linq;

namespace ComputerUse.Windows
{
    internal static class NativeDirectoryScopeTests
    {
        static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        static int Main()
        {
            try
            {
                Require(!NativeWindowDiscovery.NeedsDesktopSupplement("surfaces", null, true),
                    "A known application's surfaces must not wait for an unrelated desktop UIA provider");
                Require(!NativeWindowDiscovery.NeedsDesktopSupplement("surfaces", "selected", true),
                    "An exact window read must not enumerate desktop UIA");
                Require(NativeWindowDiscovery.NeedsDesktopSupplement("applications", null, true),
                    "Application discovery must retain its optional desktop supplement");
                Require(!NativeWindowDiscovery.NeedsDesktopSupplement("applications", null, false),
                    "A visual-only directory must not start UIA providers");
                var selected = new NativeWindowRecord { Native = new Window { Ref = "selected" } };
                var popup = new NativeWindowRecord { Native = new Window { Ref = "popup" } };
                var windows = new[] { selected, popup };
                Require(NativeRootProbe.Candidates(windows, null).SequenceEqual(windows),
                    "Skipping desktop enumeration must retain target and popup UIA root enrichment");
                Require(NativeRootProbe.Candidates(windows, "selected").SequenceEqual(new[] { selected }),
                    "A fixed-window read must retain its exact root scope");
                Console.WriteLine("Directory scope isolation passed");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }
    }
}
