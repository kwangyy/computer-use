// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace ComputerUse.Windows
{
    internal sealed partial class NativeWindowsPointer
    {
        // Preparation is bounded within the original click. Each wheel step
        // uses a live viewport and re-reads the same retained target. No trial
        // click, item lookup by label, or native Invoke is used to reveal it.
        public void PrepareVisibility(Dictionary<string, object> target, string[] within, Request r)
        {
            var selected = Element(target, within, r);
            if (!selected.VisibilityDeferred)
                return;
            NativePreparationEffect effect = null;
            bool moved = false;
            double until = Math.Min(r.Deadline, Wire.Now + 2500);
            for (int step = 0; step < 24 && selected.VisibilityDeferred && Wire.Now < until; step++)
            {
                r.Check();
                RequireIdle(r);
                var window = (NativeWindowRecord)selected.Native;
                var node = windows.Node((string)target["node"], (string)target["surface"], r);
                var viewport = ScrollViewport(node, selected.WindowBounds, r, true);
                if (viewport == null)
                    throw new Failure("input_unavailable", "the original element has no observed scroll viewport; no wheel or requested click sent");
                bool horizontal;
                int amount = NativeElementVisibility.Wheel(selected.Bounds, viewport, out horizontal);
                if (amount == 0)
                    break;
                var point = new NativePointerPoint(viewport.X + viewport.NativeWidth / 2,
                                                   viewport.Y + viewport.NativeHeight / 2);
                var hit = Native.WindowFromPoint(new Native.POINT { X = point.X, Y = point.Y });
                if (!Native.Inside(window.Native.Handle, hit))
                    throw new Failure("input_unavailable", "the original scroll viewport is covered by another window");
                var gui = new Native.GUI { Size = Marshal.SizeOf(typeof(Native.GUI)) };
                if (Native.GetGUIThreadInfo(0, ref gui) && gui.Capture != IntPtr.Zero &&
                    !Native.Inside(window.Native.Handle, gui.Capture))
                    throw new Failure("input_unavailable", "another window owns mouse capture during scroll preparation");
                NativeInputAccess.RequireForeground();
                NativeInputAccess.RequireWindow(window.Native.Handle);
                var physical = NativePointerCapture.Normalize(point, selected.Canvas);
                int accepted = NativeElementVisibility.SendWheel(r, new[] {
                    new NativePointerEvent(0xc001, physical.X, physical.Y),
                    new NativePointerEvent(horizontal ? 0x1000u : 0x800u, data: amount)
                }, Send, ref effect);
                if (accepted != 2)
                    throw new Failure("input_unavailable", "scroll preparation was not fully inserted; no requested click sent");
                Native.NoteVisualInput();
                r.Wait(50);
                var next = Element(target, within, r);
                if (effect != null)
                    effect.Observation = next.VisibilityDeferred ? "unavailable" : "confirmed";
                moved |= next.Bounds != null &&
                         (next.Bounds.X != selected.Bounds.X || next.Bounds.Y != selected.Bounds.Y);
                selected = next;
            }
            if (effect != null)
                effect.Observation = !selected.VisibilityDeferred ? "confirmed" : "unavailable";
            if (selected.VisibilityDeferred)
            {
                if (effect != null && effect.Dispatch != "notDispatched" && r.ClickPreparation != null)
                    r.ClickPreparation.RequireObservation();
                throw new Failure("input_unavailable", moved
                    ? "the original element made viewport progress but is still outside the bounded preparation; no requested click sent"
                    : "the original element did not become visible during bounded scroll preparation; no requested click sent");
            }
        }

        NativeCaptureGeometry ScrollViewport(NativeNodeRecord node, NativeCaptureGeometry fallback, Request r,
                                            bool requireScrollEvidence = false)
        {
            NativeCaptureGeometry viewport = null;
            try
            {
                var current = node.Element;
                for (int depth = 0; depth < 48; depth++)
                {
                    r.Check();
                    current = UiaClient.Parent(current);
                    if (current == null)
                        break;
                    var role = NativeUiaBoundary.Property(current, AutomationElement.ControlTypeProperty, r) as ControlType;
                    bool collection = role == ControlType.List || role == ControlType.Tree || role == ControlType.DataGrid;
                    bool scrollable = collection;
                    if (!scrollable)
                        scrollable = HasScrollEvidence(current, node.Window, r);
                    if (!scrollable)
                    {
                        if (role == ControlType.Window)
                            break;
                        continue;
                    }
                    object value = NativeUiaBoundary.Property(current, AutomationElement.BoundingRectangleProperty, r);
                    if (!(value is Rect))
                        continue;
                    var box = (Rect)value;
                    if (box.IsEmpty || !(box.Width >= 1 && box.Width <= 32768 && box.Height >= 1 && box.Height <= 32768) ||
                        !(Math.Abs(box.X) <= 100000 && Math.Abs(box.Y) <= 100000))
                        continue;
                    var bounds = new NativeCaptureGeometry((int)box.X, (int)box.Y, (int)box.Width, (int)box.Height, 1);
                    var clipped = RectangleIntersection(bounds, fallback, fallback);
                    if (clipped != null)
                        viewport = viewport == null ? clipped
                                   : RectangleIntersection(viewport, clipped, clipped) ?? clipped;
                    if (role == ControlType.Window)
                        break;
                }
            }
            catch (COMException) { r.Check(); }
            catch (ElementNotAvailableException) { r.Check(); }
            // Optional parent facts never veto the independent retained
            // element and native window route.
            return viewport ?? (requireScrollEvidence ? null : fallback);
        }

        static bool HasScrollEvidence(UiaElement element, NativeWindowRecord window, Request r)
        {
            try
            {
                if (Equals(NativeUiaBoundary.Property(element, ScrollPattern.VerticallyScrollableProperty, r), true) ||
                    Equals(NativeUiaBoundary.Property(element, ScrollPattern.HorizontallyScrollableProperty, r), true))
                    return true;
            }
            catch (COMException) { r.Check(); }
            catch (ElementNotAvailableException) { r.Check(); }
            try
            {
                var value = NativeUiaBoundary.Property(element, AutomationElement.NativeWindowHandleProperty, r);
                if (!(value is int))
                    return false;
                var handle = new IntPtr((int)value);
                // Standard scroll styles qualify the ancestor HWND, not an
                // arbitrary sibling that happens to occupy the window center.
                return handle != IntPtr.Zero && Native.Inside(window.Native.Handle, handle) &&
                       (Native.GetWindowLongPtr(handle, -16).ToInt64() & 0x00300000L) != 0;
            }
            catch (COMException) { r.Check(); }
            catch (ElementNotAvailableException) { r.Check(); }
            return false;
        }
    }

    internal static class NativeElementVisibility
    {
        internal static int SendWheel(Request r, NativePointerEvent[] events,
                                      Func<NativePointerEvent[], int> send, ref NativePreparationEffect effect)
        {
            // Cancellation before entry has no preparation record and no
            // effect. An exception after entry retains possible wheel/move
            // effects, separately from the still-undispatched requested click.
            r.Check();
            bool priorEffect = r.Effect;
            if (effect == null && r.ClickPreparation != null)
                effect = r.ClickPreparation.Preparing("scrollIntoView");
            if (effect != null)
            {
                effect.Dispatch = effect.Observation = "unknown";
                effect.Expected = (effect.Expected ?? 0) + events.Length;
            }
            r.Effect = true;
            int accepted = send(events);
            if (accepted < 0 || accepted > events.Length)
                throw new Failure("execution_unknown", "scroll preparation returned an invalid inserted-event count");
            r.Effect = priorEffect || accepted != 0;
            if (effect != null)
            {
                effect.Accepted = (effect.Accepted ?? 0) + accepted;
                effect.Dispatch = effect.Accepted == effect.Expected ? "dispatched"
                                  : effect.Accepted == 0 ? "notDispatched" : "partial";
                if (effect.Accepted == 0)
                    effect.Observation = "unavailable";
            }
            return accepted;
        }

        internal static int Wheel(NativeCaptureGeometry target, NativeCaptureGeometry viewport, out bool horizontal)
        {
            horizontal = false;
            if (target.Y >= viewport.Y + viewport.NativeHeight)
                return -360;
            if (target.Y + target.NativeHeight <= viewport.Y)
                return 360;
            horizontal = true;
            if (target.X >= viewport.X + viewport.NativeWidth)
                return 360;
            if (target.X + target.NativeWidth <= viewport.X)
                return -360;
            return 0;
        }
    }
}
