#!/usr/bin/env python3
"""One isolated, pinned-native ABI fixture. Never launches Unity or Steam."""
from __future__ import annotations

import argparse
import ctypes as C
import hashlib
import json
import os
import signal
from pathlib import Path
import struct
import time

REFERENCE_SHA256 = "85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d"
FIELDS = [
    ("element", "H"), ("temperature", "I"), ("mass", "I"),
    ("properties", "B"), ("insulation", "B"), ("strength", "B"),
    ("radiation", "I"), ("disease", "B"), ("disease_count", "i"),
    ("backwall_element", "H"), ("backwall_mass", "I"), ("backwall_temperature", "I"),
]
# Float fields are retained as IEEE words; only ABI padding is excluded.
EVENTS = [
    ("solid", "ii"), ("liquid", "i"), ("solid_substance", "i"),
    ("substance", "iHH"), ("callback", "i"), ("falling_liquid", "iHBxIIi"),
    ("dig", "iHBxIIi"), ("ore", "iHBxIIi"), ("fx", "iiI"),
    ("unstable", "iHBBIIi"), ("damage", "ii"), ("building_temperature", "iI"),
    ("mass_consumed", "iHBxIIi"), ("mass_emitted", "iHBBIIi"),
    ("disease_consumed", "iB3xi"), ("component", "ii"),
    ("removed_mass", "iHBxIIi"), ("emitted_mass", "HBxIIi"),
    ("chunk", "II"), ("chunk_melted", "i"), ("overheat", "i"),
    ("recovered", "i"), ("building_melted", "i"), ("cell_melted", "i"),
    ("backwall_changed", "i"), ("backwall_transition", "i"),
    ("disease_emitted", "B3xi"), ("disease_consumed_info", "B3xi"),
    ("radiation_consumed", "iiI"),
]


def pack(fmt: str, *values) -> bytes:
    return struct.pack("<" + fmt, *values)


def elements() -> list[dict]:
    return [
        dict(name="Vacuum", id=758759285, state=0, max_mass=0),
        dict(name="Granite", id=-105943486, state=3, shc=.79, conductivity=3.39),
        dict(name="Oxygen", id=-1528777920, state=1, shc=1.005, conductivity=.024),
        dict(name="Water", id=1836671383, state=2, shc=4.179, conductivity=.609),
        dict(name="Unobtanium", id=1838482828, state=7),
        dict(name="Void", id=-1456075980, state=0, max_mass=0),
    ]


def encode_elements(items: list[dict]) -> bytes:
    out = bytearray(pack("i", len(items)))
    for index, item in enumerate(items):
        raw = bytearray(164)
        struct.pack_into("<iHHHBB", raw, 0, item["id"], item.get("low_transition", 65535),
                         item.get("high_transition", 65535), index, item["state"], len(item.get("gradient", [])))
        values = [item.get("shc", 1), item.get("conductivity", 0), item.get("molar_mass", 32),
                  item.get("solid_surface", 25), item.get("liquid_surface", 1), item.get("gas_surface", 1),
                  item.get("flow", .1), item.get("viscosity", 1), .01, .01,
                  item.get("max_mass", 20000), item.get("low_temp", 0), item.get("high_temp", 10000), 100]
        struct.pack_into("<14f", raw, 12, *values)
        struct.pack_into("<ififHH", raw, 68, item.get("low_ore", 758759285), item.get("low_ore_conversion", 0),
                         item.get("high_ore", 758759285), item.get("high_ore_conversion", 0),
                         item.get("sublimate_index", 65535), item.get("convert_index", 65535))
        struct.pack_into("<II", raw, 88, item.get("material_properties", 0), item.get("color", 0))
        gradient = item.get("gradient", [])
        if len(gradient) > 6:
            raise ValueError("At most six gradient colors fit the element record")
        struct.pack_into("<6I", raw, 96, *(gradient + [0] * (6 - len(gradient))))
        struct.pack_into("<i7f", raw, 120, item.get("sublimate_fx", 0), item.get("sublimate_rate", 0),
                         item.get("sublimate_efficiency", 0), item.get("sublimate_probability", 0),
                         item.get("off_gas_probability", 0), item.get("light_absorption", 0),
                         item.get("radiation_absorption", 0), item.get("radiation_per_1000_mass", 0))
        struct.pack_into("<fff", raw, 152, 300, 0, 1)
        # JSON's NaN literal loses the authored payload. Keep selected thermal
        # fields as explicit wire words for independent IEEE boundary fixtures.
        offsets = dict(conductivity=16, solid_surface=24, liquid_surface=28, gas_surface=32)
        overrides = item.get("float_word_overrides", {})
        if not isinstance(overrides, dict):
            raise ValueError("Float-word overrides must be a field-to-u32 object")
        for field, word in overrides.items():
            if field not in offsets or type(word) is not int or not 0 <= word <= 0xffffffff:
                raise ValueError("Invalid authored float-word override")
            struct.pack_into("<I", raw, offsets[field], word)
        out.extend(raw)
    for item in items:
        name = item["name"].encode()
        out.extend(pack("i", len(name)) + name)
    return bytes(out)


def encode_world(case: dict, items: list[dict]) -> bytes:
    width, height = case["width"], case["height"]
    lookup = {item["name"]: index for index, item in enumerate(items)}
    cells = [dict(case.get("fill", {"element": "Oxygen", "mass": 1, "temperature": 300}))
             for _ in range(width * height)]
    for y in range(height):
        for x in range(width):
            if x in (0, width - 1) or y in (0, height - 1):
                cells[y * width + x] = dict(case.get("boundary", {
                    "element": "Unobtanium", "mass": 20000, "temperature": 300}))
    for region in case.get("regions", []):
        for y in range(region["y"], region["y"] + region.get("height", 1)):
            for x in range(region["x"], region["x"] + region.get("width", 1)):
                cells[y * width + x] = dict(region["cell"])
    out = bytearray(pack("iiIBB", width, height, case.get("seed", 42),
                         case.get("radiation", False), case.get("headless", True)))
    for cell in cells:
        out.extend(pack("HBBB3xff", lookup[cell["element"]], cell.get("properties", 0),
                        cell.get("insulation", 255), cell.get("strength", 0),
                        cell.get("temperature", 300), cell.get("mass", 1)))
    for cell in cells:
        out.extend(pack("BB2xif", cell.get("disease", 255), cell.get("infestation", 0),
                        cell.get("disease_count", 0), cell.get("growth", 0)))
    out.extend(pack("H2xff", lookup["Vacuum"], 0, 0) * len(cells))
    return bytes(out)


def encode_diseases(case: dict, element_count: int) -> bytes:
    diseases = case.get("diseases", [])
    out = bytearray(pack("ii", len(diseases), element_count))
    for index, disease in enumerate(diseases):
        name = f"AuthoredFixture{index}".encode()
        out.extend(pack("i", len(name)) + name)
        out.extend(pack("if", 10001 + index, disease.get("strength", 1)))
        out.extend(pack("4f", 200, 250, 350, 400))
        out.extend(pack("4f", 60, 600, 600, 60))
        out.extend(pack("4f", 0, .1, 100, 1000))
        out.extend(pack("4f", 60, 600, 600, 60))
        out.extend(pack("f", .01))
        out.extend(pack("6fiB", 1, disease.get("half_life", 600), 60,
                        .5, 0, 1000000, 1, 0) * element_count)
    return bytes(out)


def encode_load(image: dict, items: list[dict]) -> bytes:
    """Author native images from the documented versioned sections, not saves."""
    version = image.get("version", 15)
    width, height = image.get("width", 1), image.get("height", 1)
    by_name = {item["name"]: item["id"] for item in items}
    cells = [dict(image["cell"]) for _ in range(width * height)]
    for region in image.get("regions", []):
        for y in range(region["y"], region["y"] + region.get("height", 1)):
            for x in range(region["x"], region["x"] + region.get("width", 1)):
                cells[y * width + x] = dict(region["cell"])
    out = bytearray(pack("8siii", b"SIMSAVE\0", version, width, height))
    if version >= 14:
        halo = 0 if image.get("internal_offsets") else 1
        out.extend(pack("ii", image.get("x", 0) + halo, image.get("y", 0) + halo))
    if version >= 13:
        out.extend(pack("B", image.get("options", 0)))
    for cell in cells:
        out.extend(pack("iff", cell.get("element_hash", by_name.get(cell.get("element"), 0)),
                        cell.get("temperature", 300), cell.get("mass", 1)))
        if version >= 14:
            out.extend(pack("f", cell.get("radiation", 0)))
    if version >= 9:
        for cell in cells:
            out.extend(pack("ii", cell.get("disease_hash", 0), cell.get("disease_count", 0)))
    if version >= 15:
        for cell in cells:
            out.extend(pack("iff", cell.get("backwall_hash", by_name["Vacuum"]),
                            cell.get("backwall_mass", 0), cell.get("backwall_temperature", 0)))
    elif version >= 8:
        out.extend(pack("I", image.get("legacy_word", 0x13579bdf)) * len(cells))
    return bytes(out)


def send_interactions(native, specification):
    records = specification.get("records", [])
    data = b"".join(pack("I3H2x5f", record.get("kind", 0x6826f67d),
        record.get("a", 2), record.get("b", 6), record.get("product", 3),
        record.get("min_mass", 0), record.get("probability", 1),
        record.get("destroy", 1), record.get("required", 1), record.get("creation", 1))
        for record in records)
    borrowed = C.create_string_buffer(data) if data else None
    address = C.addressof(borrowed) if borrowed else 0
    native.send(-930289787, pack("iQ", specification.get("count", len(records)), address))
    # The official managed writer pins this memory only for the call. Poison it
    # immediately to exercise native copying rather than a retained caller view.
    if borrowed:
        C.memset(address, 0xa5, len(data))


class Native:
    def __init__(self, path: Path, role: str):
        self.message_calls = {}
        self.role = role
        self.path = path.resolve()
        self.digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if role == "reference" and self.digest != REFERENCE_SHA256:
            raise RuntimeError("Reference fingerprint is not the verified installed build")
        self.lib = C.CDLL(str(path))
        self.errors: list[dict] = []
        self.callback_type = C.CFUNCTYPE(C.c_int, C.c_int, C.c_void_p)
        self.callback = self.callback_type(self.on_error)
        self.lib.SIM_Initialize.argtypes = [self.callback_type]
        self.lib.SIM_Initialize.restype = None
        self.lib.SIM_Shutdown.argtypes = []
        self.lib.SIM_Shutdown.restype = None
        self.lib.SIM_HandleMessage.argtypes = [C.c_int, C.c_int, C.c_void_p]
        self.lib.SIM_HandleMessage.restype = C.c_void_p
        self.lib.SIM_HandleMessages.argtypes = [C.c_int, C.c_int, C.c_int, C.c_void_p]
        self.lib.SIM_HandleMessages.restype = C.c_void_p
        self.lib.SIM_BeginSave.argtypes = [C.POINTER(C.c_int), C.c_int, C.c_int]
        self.lib.SIM_BeginSave.restype = C.c_void_p
        self.lib.SIM_EndSave.argtypes = []
        self.lib.SIM_EndSave.restype = None
        self.status = getattr(self.lib, "ONISIM_GetLastStatus", None)
        if role == "candidate":
            self.lib.ONISIM_GetAbiVersion.restype = C.c_uint
            if self.lib.ONISIM_GetAbiVersion() != 1 or self.status is None:
                raise RuntimeError("Candidate must implement the project native ABI")
            if hasattr(self.lib, "ONISIM_IsReady"):
                self.lib.ONISIM_IsReady.argtypes = []
                self.lib.ONISIM_IsReady.restype = C.c_int
        if self.status:
            self.status.restype = C.c_int
            self.lib.ONISIM_GetLastError.restype = C.c_char_p
        self.lib.SIM_Initialize(self.callback)
        self.check()
        # The official release operation is idempotent, even with no buffer.
        self.lib.SIM_EndSave()
        self.check()

    def on_error(self, message_id: int, pointer: int) -> int:
        # Both official diagnostic ids are fatal. Never throw across C ABI.
        error = {"id": message_id}
        if pointer and message_id == 1:
            raw = C.string_at(pointer, 28)
            address = struct.unpack_from("<Q", raw, 8)[0]
            error["message"] = C.string_at(address).decode(errors="replace") if address else None
            file = struct.unpack_from("<Q", raw, 16)[0]
            error["file"] = C.string_at(file).decode(errors="replace") if file else None
            error["line"] = struct.unpack_from("<i", raw, 24)[0]
        self.errors.append(error)
        return 0

    def check(self):
        if self.errors:
            raise RuntimeError("Native diagnostic: " + json.dumps(self.errors))
        if self.status and (code := self.status()):
            raise RuntimeError(f"Candidate status {code}: {self.lib.ONISIM_GetLastError().decode()}")

    def send(self, message_id: int, data: bytes = b"", count: int | None = None) -> int:
        buffer = C.create_string_buffer(data) if data else None
        if count is None:
            result = self.lib.SIM_HandleMessage(message_id, len(data), buffer)
        else:
            if count < 1 or len(data) % count:
                raise ValueError("Batch requires equal-sized records")
            result = self.lib.SIM_HandleMessages(message_id, len(data) // count, count, buffer)
        self.check()
        self.message_calls[message_id] = self.message_calls.get(message_id, 0) + (count if count is not None else 1)
        return result or 0

    def snapshot(self, pointer: int, count: int) -> dict:
        if not pointer:
            raise RuntimeError("Null publication descriptor")
        descriptor = C.string_at(pointer, 496)
        out = {"frames": struct.unpack_from("<i", descriptor)[0], "arrays": {}, "events": {}}
        for index, (name, fmt) in enumerate(FIELDS):
            address = struct.unpack_from("<Q", descriptor, 4 + index * 8)[0]
            out["arrays"][name] = list(struct.unpack("<" + fmt * count,
                C.string_at(address, struct.calcsize(fmt) * count))) if address else None
        for name, offset, fmt, size in [
            ("accumulated_flow", 448, "I", 1), ("flow_texture", 456, "I", 2),
            ("liquid_texture", 464, "I", 1), ("liquid_data_texture", 472, "I", 1),
            ("material_texture", 480, "I", 1), ("exposed_texture", 488, "B", 1),
        ]:
            address = struct.unpack_from("<Q", descriptor, offset)[0]
            out["arrays"][name] = list(struct.unpack("<" + fmt * (count * size),
                C.string_at(address, struct.calcsize(fmt) * count * size))) if address else None
        for slot, (name, fmt) in enumerate(EVENTS):
            length, address = struct.unpack_from("<iQ", descriptor, 100 + slot * 12)
            if not 0 <= length <= 1_000_000 or (length and not address):
                raise RuntimeError(f"Invalid {name} span: {length}, {address}")
            raw = C.string_at(address, length * struct.calcsize("<" + fmt)) if length else b""
            out["events"][name] = [list(record) for record in struct.iter_unpack("<" + fmt, raw)]
        return out

    def wait_ready(self):
        deadline = time.monotonic() + 5
        if self.role == "reference":
            # Pinned PrepareGameData 0x32cc6 addresses FrameSync at gSim's
            # symbol address +0x210; GameSync 0x39cbe locks +0x18 and tests
            # ready at +0x10. Observe under the SAME mutex, without swapping
            # GameData or releasing the worker. No physics state is changed.
            sync = C.addressof(C.c_void_p.in_dll(self.lib, "gSim")) + 0x210
            libc = C.CDLL(None)
            lock, unlock = libc.pthread_mutex_lock, libc.pthread_mutex_unlock
            for operation in (lock, unlock):
                operation.argtypes = [C.c_void_p]
                operation.restype = C.c_int
        while time.monotonic() < deadline:
            if self.role == "candidate":
                ready = self.lib.ONISIM_IsReady()
                self.check()
            else:
                if lock(sync + 0x18):
                    raise RuntimeError("Reference readiness mutex lock failed")
                try:
                    ready = C.c_ubyte.from_address(sync + 0x10).value
                finally:
                    if unlock(sync + 0x18):
                        raise RuntimeError("Reference readiness mutex unlock failed")
            if ready == 1:
                return
            time.sleep(.001)
        raise RuntimeError("Worker readiness observation timed out")

    def save(self, replacements=1, origin=(0, 0)) -> bytes:
        if type(replacements) is not int or not 1 <= replacements <= 16:
            raise ValueError("save replacements must be an integer from 1 to 16")
        if len(origin) != 2 or any(type(value) is not int or not -(2**31) <= value < 2**31
                                   for value in origin):
            raise ValueError("save origin requires two signed 32-bit integers")
        self.last_save_buffers = []
        try:
            for _ in range(replacements):
                size = C.c_int()
                address = self.lib.SIM_BeginSave(C.byref(size), *origin)
                self.check()
                if not address or not 0 < size.value <= 64 * 1024 * 1024:
                    raise RuntimeError("Invalid save buffer")
                # BeginSave invalidates the previous pointer. Copy each image
                # before replacing it; never compare freed pointer contents.
                image = C.string_at(address, size.value)
                self.last_save_buffers.append(dict(bytes=len(image),
                    sha256=hashlib.sha256(image).hexdigest()))
        finally:
            self.lib.SIM_EndSave()
        self.check()
        self.lib.SIM_EndSave()
        self.check()
        return image


def advance(native: Native, case: dict, visible: bytes) -> dict:
    width, height = case["width"], case["height"]
    regions = case.get("active_regions", [[0, 0, width, height - 1,
                                          case.get("sunlight", 0), case.get("cosmic_radiation", 0)]])
    native.wait_ready()
    native.send(-775326397, b"".join(pack("fiiiiff", case.get("dt", .2), *region) for region in regions))
    empty = native.snapshot(native.send(1078620451, visible), width * height)
    if empty["frames"] != 0:
        raise RuntimeError("Expected the already-completed empty batch before the queued frame")
    native.wait_ready()
    snapshot = native.snapshot(native.send(1078620451, visible), width * height)
    if snapshot["frames"] != 1:
        raise RuntimeError("Publication did not complete exactly one frame")
    return empty, snapshot


def export_probe(native, name):
    snapshot = dict(arrays={}, events={}, export_contract=name)
    if name == "linux-system-info":
        native.lib.SYSINFO_Acquire.argtypes = []
        native.lib.SYSINFO_Acquire.restype = C.c_void_p
        native.lib.SYSINFO_Release.argtypes = []
        native.lib.SYSINFO_Release.restype = None
        # The Linux profile has no system-info JSON provider. Release remains
        # idempotent before/after acquisitions, even with no simulation session.
        native.lib.SIM_Shutdown()
        native.check()
        native.lib.SYSINFO_Release()
        snapshot["nonnull"] = [bool(native.lib.SYSINFO_Acquire()), bool(native.lib.SYSINFO_Acquire())]
        native.lib.SYSINFO_Release()
        native.lib.SYSINFO_Release()
        native.check()
    elif name == "intentional-debug-assert":
        signals = []
        previous = signal.signal(signal.SIGINT, lambda number, _frame: signals.append(number))
        try:
            native.lib.SIM_DebugCrash.argtypes = []
            native.lib.SIM_DebugCrash.restype = None
            native.lib.SIM_DebugCrash()
        finally:
            signal.signal(signal.SIGINT, previous)
        expected = dict(id=1, message="Assert failed: false", file="SimBindings.cpp", line=434)
        if signals != [signal.SIGINT] or native.errors != [expected]:
            raise RuntimeError("DebugCrash must raise SIGINT then report the pinned assertion once")
        snapshot.update(signals=signals, reports=native.errors[:])
        native.errors.clear()
        native.check()
    else:
        raise ValueError("Unknown export contract")
    return snapshot


def initialize_world(native, case, items):
    if case.get("allocate_only"):
        if os.environ.get("CYCLETRIM_FIXTURE_CLOCK") != "1900000000":
            raise RuntimeError("Allocate fixture requires the isolated fixed realtime clock")
        process = C.CDLL(None)
        counter = (process.CycleTrimFixture_TimeCalls if native.role == "reference"
                   else process.CycleTrimFixture_RealtimeCalls)
        counter.argtypes = []
        counter.restype = C.c_uint64
        calls = counter()
        native.send(1092408308, pack("iiBB", case["width"], case["height"],
                                    case.get("radiation", False), case.get("headless", True)))
        if counter() <= calls:
            raise RuntimeError("Allocate did not consume its controlled realtime seed")
        native.send(-1836204275)
    else:
        native.send(2062421945, encode_world(case, items))


def run(case: dict, path: Path, role: str) -> dict:
    if role == "reference" and case.get("expected_diagnostic"):
        # The pinned bad-temperature path raises SIGINT at its assert before
        # issuing the fatal report. Only this explicitly negative child permits
        # that signal to return so both official diagnostics can be observed.
        signal.signal(signal.SIGINT, lambda _signal, _frame: None)
    native = Native(path, role)
    if name := case.get("export_probe"):
        snapshots = [export_probe(native, name)]
        native.lib.SIM_Shutdown()
        native.check()
        return dict(case=case["name"], library_sha256=native.digest, role=role,
                    cell_workers=[], snapshots=snapshots, message_calls={})
    items = case.get("elements", elements())
    width, height = case["width"], case["height"]
    native.send(1108437482, encode_elements(items))
    if "interactions" in case:
        send_interactions(native, case["interactions"])
    native.send(825301935, encode_diseases(case, len(items)))
    initialize_world(native, case, items)
    if loads := case.get("loads"):
        payloads = [encode_load(image, items) for image in loads]
        if case.get("sequential_loads"):
            for payload in payloads:
                native.send(-672538170, payload)
        else:
            if len({len(payload) for payload in payloads}) != 1:
                raise ValueError("Batched load images must have equal byte lengths")
            native.send(-672538170, b"".join(payloads), len(payloads))
    pointer = native.send(-931446686)
    snapshots = [native.snapshot(pointer, width * height)]
    conduits = None
    if case.get("conduits"):
        from conduits import Conduits
        conduits = Conduits(native, items)
    visible = bytes([255]) * (width * height)
    for step in range(case.get("steps", 1)):
        native.wait_ready()
        for update in case.get("interaction_updates", []):
            if update.get("step", 0) == step:
                send_interactions(native, update)
        for command in case.get("commands", []):
            if command.get("step", 0) == step:
                native.send(command["id"], pack(command["format"], *command["values"]), command.get("count"))
        conduit_results = []
        for operation in case.get("conduits", []):
            if operation.get("step", 0) != step:
                continue
            try:
                conduit_results.append(conduits.apply(operation, snapshots[-1]))
            except RuntimeError:
                expected = case.get("expected_diagnostic")
                matched = [error for error in native.errors if expected and error["id"] == expected["id"]
                           and expected["message_contains"] in error.get("message", "")]
                if not (expected and operation["op"] == expected["op"] and len(matched) == 1
                        and all(error in matched or (role == "reference"
                            and error["id"] == 1 and expected.get("reference_assert")
                            and error.get("message") == "Assert failed: " + expected["reference_assert"])
                            for error in native.errors)):
                    raise
                snapshots[-1]["expected_diagnostic"] = expected
                # The negative fixture compares the fatal channel, not continued
                # execution after a fatal report or the reference's fallback state.
                observed = native.errors[:]
                native.errors.clear()
                conduits.Shutdown()
                native.lib.SIM_Shutdown()
                native.check()
                return {"case": case["name"], "library_sha256": native.digest, "role": role,
                        "cell_workers": [], "snapshots": snapshots, "diagnostics_observed": observed,
                        "message_calls": native.message_calls}
        empty, snapshot = advance(native, case, visible)
        if conduits:
            snapshot["conduit"] = conduit_results
        snapshots.extend((empty, snapshot))
        if count := case.get("extra_prepare_count", 0):
            pointer = native.send(1078620451, visible * count, count)
            snapshots.append(native.snapshot(pointer, width * height))
    if case.get("expected_diagnostic"):
        raise RuntimeError("Expected native diagnostic was not reported")
    for event in case.get("required_events", []):
        if not any(snapshot["events"][event] for snapshot in snapshots):
            raise RuntimeError(f"Fixture never exercised required event: {event}")
    for field in case.get("required_changed_arrays", []):
        if not any(snapshot["arrays"][field] != snapshots[0]["arrays"][field]
                   for snapshot in snapshots[1:]):
            raise RuntimeError(f"Fixture never changed required array: {field}")
    if "absent_element" in case:
        if any(case["absent_element"] in snapshot["arrays"]["element"] for snapshot in snapshots):
            raise RuntimeError("Unexpected interaction product in the normal frame path")
    if case.get("save_probe"):
        image = native.save(case.get("save_replacements", 1), case.get("save_origin", (0, 0)))
        snapshots[-1]["save_probe"] = dict(bytes=len(image), sha256=hashlib.sha256(image).hexdigest(),
                                           buffers=native.last_save_buffers)
    if case.get("save_roundtrip"):
        rounds = case.get("save_roundtrip_count", 1)
        if type(rounds) is not int or not 1 <= rounds <= 16:
            raise ValueError("save_roundtrip_count must be an integer from 1 to 16")
        for _ in range(rounds):
            image = native.save(case.get("save_replacements", 1))
            snapshots[-1]["save"] = dict(bytes=len(image), sha256=hashlib.sha256(image).hexdigest())
            if case.get("save_replacements", 1) > 1:
                snapshots[-1]["save_buffers"] = native.last_save_buffers
            native.lib.SIM_Initialize(native.callback)
            native.check()
            native.send(1108437482, encode_elements(items))
            native.send(825301935, encode_diseases(case, len(items)))
            initialize_world(native, case, items)
            native.send(-672538170, image)
            snapshots.append(native.snapshot(native.send(-931446686), width * height))
            for _ in range(3):
                snapshots.extend(advance(native, case, visible))
    cell_workers = sorted(path.read_text().strip() for path in Path("/proc/self/task").glob("*/comm")
                          if path.read_text().startswith("OniSim-cell-"))
    if conduits:
        conduits.Shutdown()
        native.check()
    native.lib.SIM_Shutdown()
    native.check()
    return {"case": case["name"], "library_sha256": native.digest, "role": role,
            "cell_workers": cell_workers, "snapshots": snapshots, "message_calls": native.message_calls}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--role", required=True, choices=["reference", "candidate"])
    parser.add_argument("--library", required=True, type=Path)
    parser.add_argument("--case", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    result = run(json.loads(args.case.read_text()), args.library, args.role)
    args.output.write_text(json.dumps(result, separators=(",", ":")) + "\n")


if __name__ == "__main__":
    main()
