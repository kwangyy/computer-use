// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;

namespace ComputerUse.Windows
{
    internal static class NativeGlobalInputTests
    {
        sealed class PrimitiveKeyboard : INativeKeyboardProvider
        {
            internal readonly List<NativeKeyEvent[]> Batches = new List<NativeKeyEvent[]>();
            public NativeKeyboardState Read(Dictionary<string, object> target, string[] within, Request r,
                                            bool text = false)
            {
                r.Check();
                if (target.Count != 2 || !Equals(target["global"], true) || within.Length != 0)
                    throw new Exception("The Start shortcut must keep its original global input scope");
                return new NativeKeyboardState { Enabled = true, Foreground = true, WindowRoute = true, Focused = true };
            }
            public void RequireIdle(NativeKeyEvent[] events, Request r) { r.Check(); }
            public void Activate(NativeKeyboardState state, Request r) { throw new Exception("Unexpected activation"); }
            public void Focus(NativeKeyboardState state, Request r) { throw new Exception("Unexpected focus change"); }
            public int Send(NativeKeyEvent[] events) { Batches.Add(events); return events.Length; }
        }

        static void CheckStartShortcut(string key, bool initiallyVisible, bool opens, string confirmation)
        {
            var target = Wire.Obj("session", "session", "global", true);
            var provider = new PrimitiveKeyboard();
            int observed = 0;
            var keyboard = new NativeKeyboard(provider, startMenuVisible: () =>
            {
                observed++;
                return initiallyVisible || opens && provider.Batches.Count != 0 && observed >= 3;
            });
            var operation = new NativeWriteOperation(target, Wire.Obj("kind", "key", "parameters", Wire.Obj("key", key)));
            var result = Wire.Object(keyboard.Execute(
                operation, Wire.Obj("kind", "global_input", "primitiveId", "global", "target", target,
                                    "foreground", false),
                new Request(CancellationToken.None, 3000, true)));
            if (provider.Batches.Count != 1 || !Equals(result["dispatch"], "dispatched") ||
                !Equals(result["confirmation"], confirmation) || result["errorCode"] != null)
                throw new Exception("Unexpected Start shortcut receipt: " + key + ", " + result["confirmation"] +
                                    ", " + result["dispatch"] + ", " + result["errorCode"]);
            if (key == "super+e" && observed != 0)
                throw new Exception("Other Windows shortcuts must not probe Start");
            if (!opens && !initiallyVisible && key == "ctrl+Escape" &&
                !((string)result["detail"]).Contains("was not observed"))
                throw new Exception("Accepted key events must not stand in for the Start menu");
        }

        static void Main()
        {
            CheckStartShortcut("ctrl+Escape", false, true, "confirmed");
            CheckStartShortcut("ctrl+Escape", false, false, "unavailable");
            CheckStartShortcut("super", false, true, "confirmed");
            CheckStartShortcut("super", true, false, "unavailable");
            CheckStartShortcut("super+e", false, false, "unavailable");
            Console.WriteLine("Start shortcuts dispatch once and distinguish the observed menu from accepted input.");
        }
    }
}
