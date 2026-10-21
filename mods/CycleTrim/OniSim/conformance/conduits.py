"""Drive the seven public conduit exports against owned ABI input arrays."""
import ctypes as C
import struct


class Conduits:
    def __init__(self, native, elements):
        self.native = native
        self.elements = {item["name"]: item["id"] for item in elements}
        self.handles = {}
        signatures = {
            "Initialize": ([], None), "Shutdown": ([], None), "Clear": ([], None),
            "Add": ([C.c_float, C.c_float, C.c_int, C.c_int, C.c_float, C.c_float, C.c_bool], C.c_int),
            # The official body returns void; managed callers discard its int declaration.
            "Set": ([C.c_int, C.c_float, C.c_float, C.c_int], None),
            "Remove": ([C.c_int], None), "Update": ([C.c_float, C.c_void_p], C.c_void_p),
        }
        for name, (arguments, result) in signatures.items():
            function = getattr(native.lib, "ConduitTemperatureManager_" + name)
            function.argtypes, function.restype = arguments, result
            setattr(self, name, function)
        self.Initialize()
        native.check()

    def apply(self, operation, snapshot):
        name = operation["op"]
        output = dict(op=name)
        if name == "add":
            handle = self.Add(operation["temperature"], operation["mass"], self.elements[operation["element"]],
                              operation.get("building", -1), operation.get("capacity", 79),
                              operation.get("conductivity", 3.39), operation.get("insulated", False))
            self.handles[operation["name"]] = handle
            output["handle"] = handle
        elif name == "set":
            self.Set(self.handles[operation["name"]], operation["temperature"], operation["mass"],
                     self.elements[operation["element"]])
        elif name == "remove":
            self.Remove(self.handles[operation["name"]])
        elif name == "clear":
            self.Clear()
        elif name == "shutdown":
            self.Shutdown()
        elif name == "initialize":
            self.Initialize()
        elif name == "update":
            records = snapshot["events"]["building_temperature"]
            raw = b"".join(struct.pack("<iI", *record) for record in records)
            buffer = C.create_string_buffer(raw) if raw and not operation.get("null_buildings") else None
            pointer = self.Update(operation.get("dt", .2), buffer)
            self.native.check()
            if not pointer:
                raise RuntimeError("Null conduit publication descriptor")
            descriptor = C.string_at(pointer, 36)
            for slot, field in enumerate(("temperatures", "frozen", "melted")):
                count, address = struct.unpack_from("<iQ", descriptor, slot * 12)
                if not 0 <= count <= 1_000_000 or (count and not address):
                    raise RuntimeError("Invalid conduit publication span")
                output[field] = list(struct.unpack("<" + ("I" if slot == 0 else "i") * count,
                    C.string_at(address, count * 4))) if count else []
        else:
            raise ValueError("Unknown conduit operation: " + name)
        self.native.check()
        return output
