"""MCP-delivered Windows pixels keep their exact targets when another window appears."""

import base64
import unittest
from dataclasses import replace
from io import BytesIO
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from PIL import Image

from computer_use.client.computer import App
from computer_use.client.execution import execution_scope
from computer_use.client.factory import session_controller
from computer_use.client.host import (
    set_action_handler, set_diagnostic_handler, set_execution_guard,
    set_observation_handler, set_preview_handler, set_text_page_handler,
)
from computer_use.interaction.agent.scope import window_scope
from computer_use.interaction.query.instances.scope import adopt, display_selection
from computer_use.interaction.read.acquisition.images.capture import WindowImage
from computer_use.interaction.read.acquisition.images.composite.pipeline import InterfaceImagePipeline
from computer_use.interaction.read.views.collection import CollectionPlan
from computer_use.interaction.read.views.pipeline import WindowPart
from computer_use.interaction.read.views.publication import publish
from computer_use.interaction.write.ui.pipeline import InterfacePointerPipeline
from computer_use.native.contracts import Binding
from computer_use.native.scopes import RunningApplication
from computer_use.protocol.captures import decode_capture
from computer_use.session import images as images_state
from computer_use.session.interfaces import WindowIdentity
from computer_use.session.observation_data import observation_data
from computer_use.session.state import AppState
from computer_use.session.windows import WindowReferences
from computer_use_mcp.runtime import Runtime


def surface(key):
    return dict(window=key, instance=f"owner:{key}", surface=f"surface:{key}", node=f"node:{key}",
                title=key, kind="window", parent=None, parentState="none", modal=False,
                nativeWindow=True, accessibility=True)


def setup():
    port = SimpleNamespace(connection=SimpleNamespace(
        session="session", platform="windows", capabilities=(), session_identity=(1, "session")),
        calls=SimpleNamespace(retired=False))
    controller = session_controller(port)
    app = App(controller, dict(selector="editor", _nativeState=AppState(
        "editor", running=RunningApplication("editor", "Editor", "session"))), _internal=True)
    facts = dict(kind="surfaces", session="session", platform="windows", application="editor",
                 observed=dict(id="directory", startedAt=1, completedAt=2), surfaces=[surface("a"), surface("b")],
                 focusedWindow="a", stopReasons=[], rootDiscoveryComplete=True, endedWindows=[],
                 provider=dict(status="alreadyEnabled", attribute=None, writeStatus=None))
    adopt(controller, app, facts)
    app._instance.anchor = display_selection(controller, app._instance, facts)
    return controller, app


def ref(app, key):
    return next(number for number, window in app._instance.windows.items() if window == key)


class WindowsImageDeliveryTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.previous_delivery = images_state.image_delivery_required()
        self.events = []
        self.runtime = Runtime(self.events.append)
        with execution_scope(self.runtime.executor):
            self.controller, self.app = setup()
            self.window = window_scope(self.controller, self.app, ref(self.app, "a"))
        self.captures = []
        self.write = SimpleNamespace(arun=AsyncMock(return_value="dispatched"))
        self.pointer = InterfacePointerPipeline(self.controller, self.write)
        self.runtime.task_namespace.update(
            app=self.app, window=self.window,
            compose=lambda *args: self.runtime.executor.call(self.compose, *args),
            observe=lambda *args: self.runtime.executor.call(self.observe, *args),
        )

    async def asyncTearDown(self):
        await self.runtime.close()
        for setter in (set_action_handler, set_diagnostic_handler, set_execution_guard,
                       set_observation_handler, set_preview_handler, set_text_page_handler):
            setter(None)
        images_state._delivery_required = self.previous_delivery

    def capture(self, ui, key, size=(100, 80)):
        item = ui._instance.members[key]
        token = "capture-" + str(len(self.captures))
        buffer = BytesIO()
        with Image.new("RGB", size, "white" if key == "a" else "black") as pixels:
            pixels.save(buffer, format="PNG")
        target = Binding("session", item["instance"], key, item["surface"], capture=token)
        png = decode_capture(dict(id=token, kind="window", scope=target.surface, startedAt=1, completedAt=2,
                                  width=size[0], height=size[1], png=base64.b64encode(buffer.getvalue()).decode()), "window")
        part = WindowPart(target, image=WindowImage(
            target, png, {}, dict(nativeSpace="screen", toNative=(1, 0, 0, 1, 0, 0))))
        image, _ = InterfaceImagePipeline(self.controller).run(ui, [part])
        ui._image, ui._context = image, key
        self.captures.append(image)
        return image

    def observe(self, ui, key):
        image = self.capture(ui, key)
        return publish(self.controller, ui, "Observed " + key, image.png)

    def compose(self, ui, keys):
        parts = []
        for index, key in enumerate(keys):
            original = self.capture(ui, key).pieces[0].image
            image = replace(original, coordinates={"nativeSpace": "screen", "toNative": (1, 0, 0, 1, index * 120, 0)})
            parts.append(WindowPart(original.target, image=image))
        canvas, _ = InterfaceImagePipeline(self.controller).run(ui, parts)
        ui._image = canvas
        self.captures.append(canvas)
        plan = CollectionPlan(keys[0], "image")
        plan.order = tuple(WindowIdentity.of(ui._instance.members[key]) for key in keys)
        for key, part in zip(keys, parts):
            identity = WindowIdentity.of(ui._instance.members[key])
            plan.requested[identity] = frozenset(("image",))
            plan.captured(ui._instance.members[key], part)
        coverage = plan.coverage(ui._instance.directory, parts, tuple(piece.window for piece in canvas.pieces))
        return publish(self.controller, ui, "Composite", canvas.png, window_coverage=coverage)

    async def execute(self, code):
        result = await self.runtime.execute(dict(code=code, executionId=len(self.events) + 1))
        self.assertFalse(result.get("isError"), result)
        return result

    async def click(self, ui, point=(20, 30)):
        with patch("computer_use.interaction.write.ui.pipeline.refresh", new_callable=AsyncMock):
            await self.pointer.run(ui, "click", (point,), {}, None)
        return self.write.arun.call_args.args[0]

    async def test_toast_preserves_host_pixels_and_target(self):
        await self.execute("observe(app, 'a')")
        result = await self.execute("compose(app, ('b', 'a'))")
        images = [block for block in result["content"] if block["type"] == "image"]
        self.assertEqual(len(images), 2)
        for block in images:
            with Image.open(BytesIO(base64.b64decode(block["data"]))) as png:
                self.assertEqual(png.size, (100, 80))
        write = await self.click(self.window)
        self.assertEqual(write.target.root_window, "a")
        self.assertEqual(write.intent.parameters, {"x": 20.0, "y": 30.0})

    async def test_member_delivery_keeps_exact_targets_and_coverage(self):
        await self.execute("observed = compose(app, ('a', 'b'))\nprint_image(observed.screenshot)")
        self.assertIsNone(self.captures[-1].delivered)
        coverage = observation_data(self.runtime.task_namespace["observed"])["window_coverage"]
        self.assertEqual([row["delivered"] for row in coverage], [("image",), ("image",)])
        for key in ("a", "b"):
            write = await self.click(window_scope(self.controller, self.app, ref(self.app, key)))
            self.assertEqual(write.target.root_window, key)
            self.assertEqual(write.intent.parameters, {"x": 20.0, "y": 30.0})

    async def test_delayed_delivery_keeps_newer_capture_authority(self):
        await self.execute("old = compose(app, ('a', 'b'))")
        original = self.captures[-1]
        await self.execute("observe(window, 'a')")
        fresh = WindowReferences.delivered_image(self.app._instance, self.window)
        result = await self.execute("print_image(old.screenshot)")
        self.assertIs(WindowReferences.delivered_image(self.app._instance, self.window), fresh)
        captions = [block["_meta"]["computer-use"]["caption"] for block in result["content"] if block["type"] == "image"]
        self.assertNotIn("Image point", captions[0])
        self.assertIn(f"Image point for window={ref(self.app, 'b')}", captions[1])
        self.assertEqual([image.generation for image in original.independent_images],
                         [original.generation, original.generation])

    async def test_other_platforms_keep_composite_coordinates(self):
        for platform in ("macos", "linux"):
            with self.subTest(platform=platform):
                self.app._instance.directory["platform"] = platform
                result = await self.execute("compose(app, ('a', 'b'))")
                self.assertEqual(sum(block["type"] == "image" for block in result["content"]), 1)
                write = await self.click(window_scope(self.controller, self.app, ref(self.app, "b")), (140, 30))
                self.assertEqual(write.target.root_window, "b")
                self.assertEqual(write.intent.parameters, {"x": 20.0, "y": 30.0})

    async def test_large_peer_does_not_resize_existing_window(self):
        capture = self.capture
        with patch.object(self, "capture", side_effect=lambda ui, key: capture(ui, key, (1500, 1500))):
            before = await self.execute("observe(app, 'a')")
            after = await self.execute("compose(app, ('b', 'a'))")
        for result, count in ((before, 1), (after, 2)):
            images = [block for block in result["content"] if block["type"] == "image"]
            self.assertEqual(len(images), count)
            for block in images:
                with Image.open(BytesIO(base64.b64decode(block["data"]))) as png:
                    self.assertEqual(png.size, (1500, 1500))
        write = await self.click(self.window, (735, 923))
        self.assertEqual(write.target.root_window, "a")
        self.assertEqual(write.intent.parameters, {"x": 735.0, "y": 923.0})
