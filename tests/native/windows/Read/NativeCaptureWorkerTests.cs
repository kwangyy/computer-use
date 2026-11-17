// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;

namespace ComputerUse.Windows
{
    internal static class NativeCaptureWorkerTests
    {
        sealed class Worker : INativeWorkerProcess
        {
            internal int Sends;
            internal bool Blocked, WrongId, RefuseExit, Error;
            internal Action OnPoll;
            object id;
            public bool ExitConfirmed { get; private set; }
            public void Send(object value) { id = Wire.Object(value)["id"]; Sends++; }
            public string Poll(int milliseconds)
            {
                if (OnPoll != null) OnPoll();
                if (Blocked) { Thread.Sleep(milliseconds); return null; }
                if (Error) return Wire.Json(Wire.Obj("id", id, "error", "device unavailable"));
                return Wire.Json(Wire.Obj("id", WrongId ? -1 : id, "png", "frame-" + Sends));
            }
            public bool Stop(int grace = 0) { ExitConfirmed = !RefuseExit; return ExitConfirmed; }
            public void Dispose() { }
        }
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static Dictionary<string, object> Parameters() { return Wire.Obj("route", "wgc"); }
        static Request Request() { return new Request(CancellationToken.None, 3000); }
        static void Failure(Action action)
        {
            try { action(); }
            catch (Failure) { return; }
            throw new Exception("Expected explicit capture failure");
        }

        static int Main()
        {
            try { Run(); return 0; }
            catch (Exception error) { Console.WriteLine(error.Message); return 1; }
        }

        static void Run()
        {
            int starts = 0;
            var first = new Worker();
            using (var capture = new NativeCaptureWorker(() => { starts++; return first; }))
            {
                var a = capture.Exchange(Parameters(), Request(), 500);
                var b = capture.Exchange(Parameters(), Request(), 500);
                Require(starts == 1 && first.Sends == 2 && !first.ExitConfirmed,
                        "Warm captures must reuse the healthy helper instead of starting per frame");
                Require((string)a["png"] == "frame-1" && (string)b["png"] == "frame-2",
                        "Every warm capture requires a new reply");
            }
            Require(first.ExitConfirmed, "Closing the owner stops the helper");
            foreach (string mode in new[] { "timeout", "wrong_id", "error" })
            {
                var broken = new Worker { Blocked = mode == "timeout", WrongId = mode == "wrong_id", Error = mode == "error" };
                var healthy = new Worker();
                starts = 0;
                using (var capture = new NativeCaptureWorker(() => ++starts == 1 ? broken : healthy))
                {
                    Failure(() => capture.Exchange(Parameters(), Request(), 30));
                    Require(broken.ExitConfirmed, "Failed requests must stop their helper before replacement");
                    capture.Exchange(Parameters(), Request(), 500);
                    Require(starts == 2 && healthy.Sends == 1, "Next capture must use a fresh healthy helper");
                }
            }
            using (var cancellation = new CancellationTokenSource())
            {
                var blocked = new Worker { Blocked = true, OnPoll = () => cancellation.Cancel() };
                using (var capture = new NativeCaptureWorker(() => blocked))
                {
                    Failure(() => capture.Exchange(Parameters(), new Request(cancellation.Token, 3000), 500));
                    Require(blocked.ExitConfirmed, "Cancellation must stop the active capture helper");
                }
            }
            var unconfirmed = new Worker { WrongId = true, RefuseExit = true };
            starts = 0;
            var retired = new NativeCaptureWorker(() => { starts++; return unconfirmed; });
            Failure(() => retired.Exchange(Parameters(), Request(), 500));
            Failure(() => retired.Exchange(Parameters(), Request(), 500));
            Require(starts == 1, "Unconfirmed exit must never permit another capture helper");
            Failure(() => retired.Dispose());
            Console.WriteLine("Capture worker reuse, freshness, timeout, cancellation, identity and retirement passed.");
        }
    }
}
