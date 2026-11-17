"""Windows compact frames retain the same caller-visible facts in the new Rust core."""

from copy import deepcopy
import json

import pytest

from computer_use.protocol.observation.adapters import decode_query_facts
from computer_use.protocol.observation.adapters.wire import DriverWire
from test_read_rust_transport import _observation


def frames():
    inline = _observation("windows")
    props = inline["nodes"][0]["properties"]
    props[1]["read"] = {"complete": False, "issues": [{"reason": "timeout", "path": ["Name"]}]}
    props.append({"key": ["windows.uia", "HelpText"], "value": None, "state": "unsupported"})
    compact = deepcopy(inline)
    compact["readMetadata"] = [compact["nodes"][0]["properties"][1].pop("read")]
    compact["nodes"][0]["properties"][1]["read"] = {"metadataRead": 0}
    compact["propertyPool"] = compact["nodes"][0]["properties"]
    compact["nodes"][0]["properties"] = [{"property": index} for index in range(len(compact["propertyPool"]))]
    return inline, compact


def capture(extension, raw):
    frame = extension.open_read_frame("json", json.dumps(raw).encode(), 1024 * 1024)
    return frame, frame.capture("window", [])


def test_compact_frame_matches_inline_and_keeps_original_diagnostic(native_read):
    inline, compact = frames()
    _, left = capture(native_read, inline)
    frame, right = capture(native_read, compact)
    assert left.source_tokens == right.source_tokens
    assert left.node_view("root", ["role", "name"]) == right.node_view("root", ["role", "name"])
    assert frame.raw_result() == compact
    assert decode_query_facts(compact) == decode_query_facts(inline)


@pytest.mark.parametrize("bad", [True, -1, 1.0, 50, "0"])
@pytest.mark.parametrize("marker", ["property", "metadataRead"])
def test_invalid_compact_indices_fail_in_both_decoders(native_read, bad, marker):
    _, raw = frames()
    if marker == "metadataRead":
        raw["propertyPool"][1]["read"] = {marker: bad}
    else:
        raw["nodes"][0]["properties"][0] = {marker: bad}
    with pytest.raises(ValueError):
        capture(native_read, raw)
    with pytest.raises(ValueError):
        decode_query_facts(raw)


@pytest.mark.parametrize("platform", ["macos", "linux"])
def test_windows_property_pools_do_not_change_other_platforms(native_read, platform):
    raw = _observation(platform)
    capture(native_read, raw)
    assert decode_query_facts(raw)["platform"] == platform
    raw["propertyPool"] = []
    with pytest.raises(ValueError):
        capture(native_read, raw)
    with pytest.raises(ValueError):
        decode_query_facts(raw)


def test_pool_cannot_hide_native_references_and_read_copies_are_independent(native_read):
    _, raw = frames()
    wire = DriverWire(raw)
    first = wire.property({"property": 1})
    first.read["issues"][0]["path"].append("changed")
    assert wire.property({"property": 1}).read["issues"][0]["path"] == ["Name"]
    raw["propertyPool"][0]["value"] = {"nativeType": "UIAElement", "data": {"ref": "root"}}
    with pytest.raises(ValueError):
        capture(native_read, raw)
    with pytest.raises(ValueError):
        decode_query_facts(raw)
