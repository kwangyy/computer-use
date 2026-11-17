"""Windows presentation regressions through the Rust capture and public document."""

import json
from copy import deepcopy
from types import SimpleNamespace
from unittest.mock import Mock

import pytest

from computer_use.interaction.read.accessibility.preparation import (
    PrepareSnapshotPipeline, SnapshotRenderRequest, WindowDisclosureContext,
)
from computer_use.interaction.read.accessibility.snapshot import NativeSnapshot
from computer_use.interaction.read.presentation.document import DisplayDocument
from computer_use.interaction.read.delivery.diff.formatting import plan_document_diff
from test_read_rust_transport import _observation
from computer_use_mcp.output import ObservationPresenter, OutputBuffer
from computer_use_mcp.runtime import Runtime


def document(extension, raw, *, details=False, confirmed=True):
    frame = extension.open_read_frame("json", json.dumps(raw).encode(), 1024 * 1024)
    capture = frame.capture("window", [])
    snapshot = NativeSnapshot.number(capture, {token: str(i) for i, token in enumerate(capture.source_tokens)})
    context = WindowDisclosureContext(0, True, False, scope_confirmed=confirmed)
    prepared = PrepareSnapshotPipeline().run(
        SnapshotRenderRequest(snapshot, compact=True, public_api=True, window_context=context)
    )
    return DisplayDocument.tree(prepared, details=details).scoped(("session", None, None, "surface", None))


def relation(raw, name, *, total=0, node=0, targets=()):
    source = raw["nodes"][node]["source"]
    raw["nodes"][node]["properties"].append({
        "key": [source, name], "value": {"nativeType": "array", "data": {"items": [
            {"nativeType": raw["nodes"][node]["nativeType"], "data": {"ref": target}} for target in targets
        ]}},
        "read": {"complete": total == len(targets), "details": [
            [key, {"nativeType": "provider", "data": value}]
            for key, value in {"relation": True, "certainty": "confirmed", "total": total,
                               "state": "complete" if total == len(targets) else "partial"}.items()
        ]},
    })


def test_confirmed_empty_windows_descriptions_are_kept_in_details(native_read):
    raw = _observation("windows")
    relation(raw, "DescribedBy")
    assert "Description of" not in document(native_read, raw).text
    assert "Description of" in document(native_read, raw, details=True).text


@pytest.mark.parametrize("flag", [False, True])
def test_windows_default_state_is_compact_without_hiding_true_state(native_read, flag):
    raw = _observation("windows")
    raw["nodes"][0]["properties"][0]["value"]["data"] = "ControlType.Button"
    raw["nodes"][0]["properties"].extend([
        {"key": ["windows.uia", key], "value": {"nativeType": "Boolean", "data": flag}}
        for key in ("IsPassword", "IsOffscreen")
    ])
    text = document(native_read, raw).text
    assert ("Protected:" in text) == flag
    assert ("Offscreen:" in text) == flag
    assert "protected" in document(native_read, raw, details=True).text.lower()


@pytest.mark.parametrize("platform,confirmed,repeat", [
    ("windows", True, False), ("windows", False, True), ("macos", True, True), ("linux", True, True),
])
def test_unchanged_partial_windows_context_does_not_repeat(native_read, platform, confirmed, repeat):
    raw = _observation(platform)
    children = {"windows": "RawViewChildren", "macos": "AXChildren", "linux": "children"}[platform]
    role = {"windows": "ControlType.Button", "macos": "AXButton", "linux": "push button"}[platform]
    for token in ("partial", "changed"):
        node = deepcopy(raw["nodes"][0])
        node["id"] = token
        node["properties"][0]["value"]["data"] = role
        node["properties"][1]["value"]["data"] = token
        raw["nodes"].append(node)
    relation(raw, children, total=2, targets=("partial", "changed"))
    relation(raw, children, total=3, node=1)
    relation(raw, children, node=2)
    before = document(native_read, raw, confirmed=confirmed)
    refs = {str(i): node["id"] for i, node in enumerate(raw["nodes"])}
    raw["nodes"][2]["properties"][1]["value"]["data"] = "Updated"
    plan = plan_document_diff(before, document(native_read, raw, confirmed=confirmed), refs, refs)
    assert plan.mode == "diff"
    assert ("1" in plan.tree_at(0).context) == repeat
    # A real coverage change still reaches the caller.
    raw["nodes"][1]["properties"][-1]["read"]["details"][2][1]["data"] = 4
    changed = plan_document_diff(before, document(native_read, raw, confirmed=confirmed), refs, refs)
    assert "1" in changed.tree_at(0).changes


@pytest.mark.parametrize("platform,full", [
    ("windows", False), ("windows", True), ("macos", False), ("linux", False),
])
def test_large_tree_within_shared_budget_establishes_its_delivered_baseline(native_read, platform, full):
    text = "Captured label\n" * 700 + "Observation coverage: partial native enumeration\n"
    observation = {"document": document(native_read, _observation(platform)), "full": full, "state": object()}
    output = OutputBuffer()
    presenter = SimpleNamespace(trees=ObservationPresenter.trees, text=lambda _: text, delivered=Mock())
    runtime = Runtime.__new__(Runtime)
    runtime.presenter, runtime.task_namespace = presenter, {}
    runtime._next_captured_observation, runtime._captured_observations = 0, []
    runtime.emit = output.append
    run = {"output": output, "deliveredObservations": []}
    Runtime.publish_observation(runtime, run, observation, 0)
    delivered = output.content[0]["text"]
    assert delivered == text
    presenter.delivered.assert_called_once_with(observation)
    assert run["deliveredObservations"] == [observation["state"]]
    assert runtime.task_namespace == {}
