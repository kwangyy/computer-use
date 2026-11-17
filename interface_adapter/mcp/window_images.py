# Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

"""Deliver Windows captures in each window's own stable image coordinates."""

from dataclasses import replace

from computer_use.identity import Record
from computer_use.interaction.read.acquisition.images.composite.pipeline import InterfaceImagePipeline
from computer_use.protocol.captures import capture_metadata
from computer_use.session.images import _source, register_image_delivery
from computer_use.session.interfaces import WindowIdentity
from computer_use.session.surface_content import display_host


def independent_window_images(png):
    """Reuse captured pixels; never recapture, crop, or infer a new target."""
    known = _source(png)
    source = known["source"] if known is not None else {}
    if source.get("platform") != "windows":
        return ()
    canvas = source.get("coordinateImage", lambda: None)()
    controller = source.get("coordinateController", lambda: None)()
    state = source.get("coordinateOwner", lambda: None)()
    if canvas is None or controller is None or state is None:
        return ()
    if len(canvas.pieces) < 2:
        return ()
    # Different historical states of the same window must remain explicitly
    # disambiguated by the original canvas, never compete for one point alias.
    if len({piece.window for piece in canvas.pieces}) != len(canvas.pieces):
        return ()
    # An attached menu and its host share a public point alias even though
    # they have distinct HWNDs. Keep their existing composite before child
    # registration can replace either member's delivered coordinate mapping.
    hosts = dict(canvas.content_hosts)
    captured = {identity.window: {"content": {"host": hosts.get(identity.window)}} for identity in canvas.identities}
    aliases = {window: ref for ref, window in state.windows.items()}
    assigned = [aliases.get(display_host(captured, piece.window)) for piece in canvas.pieces]
    assigned = [ref for ref in assigned if ref is not None]
    if len(set(assigned)) != len(assigned):
        return ()
    if not canvas.independent_images:
        pipeline = InterfaceImagePipeline(controller)
        children = []
        for piece in canvas.pieces:
            image = piece.image
            stamp = capture_metadata(image.image)
            local = replace(piece, box=(0, 0, stamp["width"], stamp["height"]))
            child = replace(canvas, png=image.image, pieces=(local,), origin=(0, 0), scale=1,
                            delivered=None, digest=b"", delivered_coordinates=None, generation=0,
                            source_canvas=canvas, independent_images=())
            identity = WindowIdentity(piece.window, image.target.surface, image.target.instance)
            alias = next((ref for ref, window in state.windows.items() if window == piece.window), None)
            summary = pipeline.metadata(child, ((identity, image),), (identity,), "single",
                                        {identity: alias}, {identity: piece.scope_confirmed})
            ui = Record(_instance=state, _anchor=piece.window, _window_identity=image.target, _image=None)
            pipeline.register(ui, child, image.target, summary, historical=(),
                              has_confirmed_pixels=piece.scope_confirmed)
            children.append(child)
        canvas.independent_images = tuple(children)

    children = canvas.independent_images
    remaining = {id(child.png) for child in children}

    def acknowledge(key):
        remaining.discard(key)
        if not remaining:
            # Account for all displayed source windows without acknowledging
            # the undisplayed composite's coordinate system.
            for callback in known.pop("delivery_callbacks", ()):
                callback()

    for child in children:
        key = id(child.png)
        register_image_delivery(child.png, lambda key=key: acknowledge(key))
    return tuple(child.png for child in children)
