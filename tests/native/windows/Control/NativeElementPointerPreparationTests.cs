// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;

namespace ComputerUse.Windows
{
    internal static class NativeElementPointerPreparationTests
    {
        sealed class Objects : INativeObjectProvider
        {
            public IDisposable Acquire(Request r) { throw new Exception("No object dispatch"); }
            public NativeObjectState Read(Dictionary<string, object> target, string[] within, Request r)
            { return new NativeObjectState { Role = "ControlType.Group", Enabled = true, ActionsComplete = true, Actions = new string[0] }; }
            public Action Prepare(NativeObjectState state, string unit, string action, string text, Request r)
            { throw new Exception("An offscreen row must not be invoked"); }
        }

        sealed class Pointer : INativePointerProvider, INativeElementPointerProvider, INativeElementVisibilityProvider
        {
            internal bool Visible, Foreground = true, Fail, Obstructed, AccessDenied;
            internal NativeElementHitKind Hit = NativeElementHitKind.Target;
            internal int Prepares, Activations, Sends;
            internal NativePointerEvent[] Events;
            public NativePointerCapture Capture(Dictionary<string, object> target, Request r)
            { throw new Exception("No image required for a retained row"); }
            public NativePointerState Read(Dictionary<string, object> target, NativePointerPoint point, NativePointerGestureIntent gesture, bool dragging, Request r)
            { throw new Exception("No captured input"); }
            public NativeElementPointerTarget Element(Dictionary<string, object> target, string[] within, Request r)
            {
                return new NativeElementPointerTarget { GeometryDeferred = !Visible, VisibilityDeferred = !Visible,
                    Point = new NativePointerPoint(30, 40), Canvas = new NativeCaptureGeometry(0, 0, 100, 100, 1),
                    Hit = new NativeElementHit(Hit) };
            }
            public NativePointerState ReadElement(Dictionary<string, object> target, string[] within, NativeElementPointerTarget point, NativePointerGestureIntent gesture, Request r)
            {
                var route = new NativePointerState { ForegroundReady = Visible && Foreground, Foreground = Foreground, PhysicalReady = Visible && !Obstructed, Recipient = "original" };
                route.Ready = NativePointerClickAdmission.ElementReady(route, point.Hit);
                return route;
            }
            public void PrepareVisibility(Dictionary<string, object> target, string[] within, Request r)
            {
                r.Check(); Prepares++;
                if (Fail) throw new Failure("input_unavailable", "bounded preparation did not reveal original row");
                var effect = r.ClickPreparation.Preparing("scrollIntoView");
                effect.Dispatch = "dispatched"; effect.Observation = "confirmed";
                Visible = true;
            }
            public void RequireIdle(Request r)
            {
                r.Check();
                if (AccessDenied && Foreground)
                    NativeInputAccess.RequireIntegrity(8192, 12288, "foreground window", 27396);
            }
            public void Activate(NativePointerState state, Request r) { Activations++; Foreground = true; }
            public int Send(NativePointerEvent[] events) { Sends++; Events = events; return events.Length; }
        }

        static void Check(bool condition, string detail)
        { if (!condition) throw new Exception(detail); }

        static Dictionary<string, object> Click(Pointer pointer, bool permitPreparation = true)
        {
            var target = Wire.Obj("session", "session", "instance", "app", "rootWindow", "root", "surface", "surface", "node", "row", "capture", null);
            var operation = new NativeWriteOperation(target, Wire.Obj("kind", "click", "parameters", Wire.Obj()), true);
            var engine = new NativeExecution(new Objects(), pointer: new NativePointer(pointer));
            var r = new Request(CancellationToken.None, 2000);
            var facts = Wire.Object(engine.Inspect(operation, target, new string[0], r));
            Check(pointer.Prepares == 0 && pointer.Activations == 0 && pointer.Sends == 0, "Inspection must not scroll or focus");
            Dictionary<string, object> route = null;
            foreach (var raw in Wire.Array(facts["primitives"]))
            {
                var item = Wire.Object(raw);
                if ((string)item["unit"] == "pointer_sequence") route = item;
            }
            Check(route != null, "Offscreen row must retain a physical preparation offer");
            return Wire.Object(engine.Execute(operation, Wire.Obj("kind", "pointer_sequence", "primitiveId", route["id"], "target", target, "foreground", permitPreparation), r));
        }

        static void Main()
        {
            try { Run(); }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                Environment.Exit(1);
            }
        }

        static void Run()
        {
            CheckScrollPreparationProgress();
            var current = new Pointer();
            var result = Click(current);
            Check(result["errorCode"] == null && current.Prepares == 1 && current.Sends == 1, "Scroll then click must complete in one call");
            Check(current.Activations == 0, "Already foreground viewport must not be activated again");
            Check(current.Events.Length == 3 && current.Events[1].Flags == 2 && current.Events[2].Flags == 4, "Only one paired click after visibility preparation");
            var inactive = new Pointer { Foreground = false };
            Check(Click(inactive)["errorCode"] == null && inactive.Activations == 1 && inactive.Prepares == 1, "Activate before revealing the same row");
            var failed = new Pointer { Fail = true };
            Check(Click(failed)["errorCode"] != null && failed.Sends == 0, "Unsuccessful reveal must not click an unrelated point");
            var denied = new Pointer();
            Check(Click(denied, false)["errorCode"] != null && denied.Prepares == 0 && denied.Sends == 0, "Preparation needs selected authority");
            var visible = new Pointer { Visible = true };
            Check(Click(visible)["errorCode"] == null && visible.Prepares == 0 && visible.Sends == 1, "Visible elements do not scroll");
            var restricted = new Pointer { Visible = true, Foreground = false, AccessDenied = true };
            var restrictedResult = Click(restricted);
            Check(Equals(restrictedResult["errorCode"], "input_unavailable") &&
                  Equals(restrictedResult["dispatch"], "notDispatched") && restricted.Sends == 0 &&
                  ((string)restrictedResult["detail"]).Contains("User assistance required: Notify the user now."),
                  "A permission refusal after activation must keep user guidance in the delivered pointer receipt");
            var contradictory = new Pointer { Visible = true, Hit = NativeElementHitKind.OtherControl };
            var contradictoryResult = Click(contradictory);
            Check(contradictoryResult["errorCode"] == null && contradictory.Sends == 1, "Same-HWND contradictory UIA hit must not veto a native geometric route");
            var normalized = NativePointerCapture.Normalize(new NativePointerPoint(30, 40), new NativeCaptureGeometry(0, 0, 100, 100, 1));
            Check(contradictory.Events[0].X == normalized.X && contradictory.Events[0].Y == normalized.Y, "Contradictory UIA must not change the retained target coordinate");
            Check(((string)contradictoryResult["detail"]).Contains("visual hit is unconfirmed"), "Receipt preserves the contradictory provider evidence");
            var obstructed = new Pointer { Visible = true, Obstructed = true, Hit = NativeElementHitKind.OtherControl };
            Check(Click(obstructed)["errorCode"] != null && obstructed.Sends == 0, "A foreign native window still blocks the independent geometry route");
            bool horizontal;
            var viewport = new NativeCaptureGeometry(1415, 416, 144, 513, 1);
            Check(NativeElementVisibility.Wheel(new NativeCaptureGeometry(1419, 2817, 135, 45, 1), viewport, out horizontal) < 0 && !horizontal, "Observed time row below viewport requires downward wheel");
            Check(NativeElementVisibility.Wheel(new NativeCaptureGeometry(1419, 420, 135, 45, 1), viewport, out horizontal) == 0, "Visible row must stop scrolling");
            var route = new NativePointerState { PhysicalReady = true, ForegroundReady = true };
            Check(NativePointerClickAdmission.ElementReady(route, new NativeElementHit(NativeElementHitKind.Descendant)), "Child label is a valid pointer hit");
            Check(NativePointerClickAdmission.ElementReady(route, new NativeElementHit(NativeElementHitKind.OtherControl)), "Same-HWND contradictory identity is not native occlusion proof");
            Console.WriteLine("NativeElementPointerPreparationTests: passed (deferred visibility, one click, preparation effects, hit boundaries)");
        }

        static void CheckScrollPreparationProgress()
        {
            var events = new[] { new NativePointerEvent(0xc001, 1, 2), new NativePointerEvent(0x800, data: -360) };
            var progress = new NativeClickProgress();
            var r = new Request(CancellationToken.None, 1000) { ClickPreparation = progress };
            NativePreparationEffect effect = null;
            Check(NativeElementVisibility.SendWheel(r, events, input => 0, ref effect) == 0 && !r.Effect && !r.Dispatched,
                  "Zero accepted preparation events must not report an input or context effect");
            Check(effect.Dispatch == "notDispatched" && !progress.HasRequestedInput,
                  "Preparation zero insertion remains distinct from the requested click");
            var evidence = progress.Evidence(Wire.Obj(), "windows.sendInput", "preparationUnavailable");
            var prep = Wire.Object(Wire.Array(evidence["preparationEvidence"])[0]);
            Check((string)prep["effect"] == "contextOnly" && (string)prep["continuation"] == "withinScope",
                  "Proven zero insertion must not veto an independent route");
            r.Effect = true;
            NativeElementVisibility.SendWheel(r, events, input => 0, ref effect);
            Check(r.Effect, "Zero wheel insertion must preserve an earlier activation effect");

            progress = new NativeClickProgress();
            r = new Request(CancellationToken.None, 1000) { ClickPreparation = progress };
            effect = null;
            NativeElementVisibility.SendWheel(r, events, input => 1, ref effect);
            Check(r.Effect && !r.Dispatched && effect.Dispatch == "partial" && effect.Accepted == 1,
                  "An accepted pointer move is a real partial preparation, not a requested click");
            NativeElementVisibility.SendWheel(r, events, input => 0, ref effect);
            Check(r.Effect && effect.Dispatch == "partial" && effect.Accepted == 1 && effect.Expected == 4,
                  "A later zero result must not erase the earlier partial effect");

            var cancelled = new CancellationTokenSource();
            progress = new NativeClickProgress();
            r = new Request(cancelled.Token, 1000) { ClickPreparation = progress };
            effect = null;
            cancelled.Cancel();
            try
            {
                NativeElementVisibility.SendWheel(r, events, input => { throw new Exception("Must not enter send"); }, ref effect);
                throw new Exception("Cancellation must be reported");
            }
            catch (Failure error) { Check(error.Code == "cancelled", "Cancellation keeps its error code"); }
            Check(effect == null && !r.Effect && progress.Preparation.Count == 0,
                  "Cancellation before send has no invented preparation effect");

            cancelled = new CancellationTokenSource();
            progress = new NativeClickProgress();
            r = new Request(cancelled.Token, 1000) { ClickPreparation = progress };
            effect = null;
            NativeElementVisibility.SendWheel(r, events, input => { cancelled.Cancel(); return 2; }, ref effect);
            try { r.Check(); throw new Exception("Post-send cancellation must stop the click"); }
            catch (Failure error) { Check(error.Code == "cancelled", "Post-send cancellation keeps its error code"); }
            Check(r.Effect && !r.Dispatched && effect.Dispatch == "dispatched" && effect.Observation == "unknown",
                  "Cancellation after insertion preserves scroll effect and unknown observation");

            progress = new NativeClickProgress();
            r = new Request(CancellationToken.None, 1000) { ClickPreparation = progress };
            effect = null;
            try
            {
                NativeElementVisibility.SendWheel(r, events, input => { throw new InvalidOperationException("transport"); }, ref effect);
                throw new Exception("Transport exception must not disappear");
            }
            catch (InvalidOperationException) { }
            Check(r.Effect && !r.Dispatched && effect.Dispatch == "unknown" && effect.Observation == "unknown",
                  "A thrown transport preserves possible wheel effects without claiming requested click dispatch");
        }
    }
}
