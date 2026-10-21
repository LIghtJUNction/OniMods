"""Validate a completed full-buffer property-texture observation."""
import re


LAYOUT = {
    "Flow": (8, "RGFloat"),
    "Liquid": (4, "RGBA32"),
    "LiquidData": (4, "RGBA32"),
    "MaterialData": (4, "RGBA32"),
    "ExposedToSunlight": (1, "R8"),
}


def validate_observation(data, request):
    def require(condition, message):
        if not condition:
            raise ValueError(message)

    require(type(request) is int and request > 0, "Invalid expected request")
    require(type(data.get("request")) is int and data["request"] == request,
            "Texture request does not match")
    require(data.get("complete") is True and data.get("passed") is True,
            "Texture observation did not pass")
    require(isinstance(data.get("graphicsDevice"), str)
            and data["graphicsDevice"] not in ("", "Null"), "No graphics device")
    width, height = data.get("width"), data.get("height")
    require(type(width) is int and type(height) is int and width > 0 and height > 0,
            "Invalid texture dimensions")
    textures = data.get("textures")
    require(isinstance(textures, dict) and set(textures) == set(LAYOUT),
            "Expected all five property textures")
    for name, (stride, texture_format) in LAYOUT.items():
        item = textures[name]
        require(isinstance(item, dict), f"{name}: invalid record")
        require(item.get("format") == texture_format, f"{name}: wrong format")
        for field in ("bytes", "gpuBytes"):
            require(type(item.get(field)) is int and item[field] == width * height * stride,
                    f"{name}: incomplete {field}")
        require(all(item.get(key) is True for key in ("cpuEqual", "gpuEqual", "gpuComplete")),
                f"{name}: comparison incomplete")
        hashes = [item.get(key) for key in ("nativeSha256", "unityCpuSha256", "gpuSha256")]
        require(all(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value)
                    for value in hashes) and len(set(hashes)) == 1,
                f"{name}: hashes do not match")
    return data
