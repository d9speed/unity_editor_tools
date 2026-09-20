"""Verify actual HEVC/ProRes output from the dedicated Unity validation project.

Usage: python tools/verify_nvenc_outputs.py <project>/Logs/nvenc_smoke
Requires FFmpeg/FFprobe on PATH, numpy and Pillow. Does not modify recordings.
"""
import json
from pathlib import Path
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image


root = Path(sys.argv[1]).resolve()
ffmpeg = shutil.which("ffmpeg")
ffprobe = shutil.which("ffprobe")
assert ffmpeg and ffprobe, "FFmpeg and FFprobe are required"
smoke = json.loads((root / "smoke_results.json").read_text(encoding="utf-8-sig"))
assert smoke["passed"], smoke


def probe(movie, codec, count, fps):
    result = json.loads(subprocess.check_output([
        ffprobe, "-v", "error", "-select_streams", "v:0", "-count_frames",
        "-show_streams", "-show_packets", "-of", "json", str(movie)]))
    stream, packets = result["streams"][0], result["packets"]
    assert stream["codec_name"] == codec, stream
    assert int(stream["nb_read_frames"]) == len(packets) == count
    assert all(abs(float(packet["pts_time"]) - i / fps) < 1e-5 for i, packet in enumerate(packets))
    return stream


reports = []
metadata = sorted(root.glob("hevc_*.capture.json"))[-1]
capture = json.loads(metadata.read_text(encoding="utf-8"))
movie = Path(capture["options"]["output_path"])
stream = probe(movie, "hevc", 180, 60)
assert stream["width"] == 1920 and stream["height"] == 1080
assert capture["stats"]["raw_readback_bytes"] == 0
assert capture["stats"]["written"] == capture["requested_frames"] == 180
reference = np.asarray(Image.open(str(movie) + ".reference.png").convert("RGB"), dtype=float)
decoded = np.frombuffer(subprocess.check_output([
    ffmpeg, "-v", "error", "-i", str(movie), "-frames:v", "1", "-f", "rawvideo",
    "-pix_fmt", "rgb24", "pipe:1"]), dtype=np.uint8).reshape(1080, 1920, 3).astype(float)
rgb_mae = float(np.abs(reference - decoded).mean())
assert rgb_mae < 4, ("HEVC RGB/orientation", rgb_mae)
reports.append({"format": "hevc", "frames": 180, "fps": 60, "width": 1920, "height": 1080,
                "rgb_mae": rgb_mae, "raw_readback_bytes": 0, "passed": True})

latest = {}
for metadata in sorted(root.glob("*.alpha_test.json")):
    data = json.loads(metadata.read_text(encoding="utf-8"))
    latest[data["vulkan"], data["camera"]] = data
assert len(latest) == 2, "Expected CPU Camera and Vulkan-preferred RenderTexture runs"
for data in latest.values():
    movie = Path(data["output"])
    stream = probe(movie, "prores", data["frames"], data["fps"])
    assert stream["profile"] == "4444" and stream["pix_fmt"].startswith("yuva444p"), stream
    assert data["clock_restored"] and data["camera_restored"]
    assert data["raw_readback_bytes"] == data["width"] * data["height"] * 4 * data["frames"]
    reference = np.asarray(Image.open(str(movie) + ".reference.png").convert("RGBA"), dtype=float)
    mask = reference[:, :, 3] >= 32
    pngs = sorted(Path(data["png_directory"]).glob("frame_*.png"))
    assert len(pngs) == data["frames"]
    errors, alpha_errors = [], []
    for index, png in enumerate(pngs):
        frame = np.asarray(Image.open(png).convert("RGBA"), dtype=float)
        expected = reference.copy()
        expected[:, :, 1][mask] += index % 40 * 3
        errors.append(float(np.abs(expected[:, :, :3] - frame[:, :, :3])[mask].mean()))
        alpha_errors.append(float(np.abs(expected[:, :, 3] - frame[:, :, 3]).max()))
        assert errors[-1] < 4 and alpha_errors[-1] <= 2, (movie.name, index, errors[-1], alpha_errors[-1])
        assert abs(float(frame[90, 560, 1]) - (100 + index % 40 * 3)) <= 3, (movie.name, index, "frame order")
    reports.append({"format": "prores_4444", "vulkan": data["vulkan"], "camera": data["camera"],
                    "frames": data["frames"], "fps": data["fps"], "width": data["width"], "height": data["height"],
                    "max_rgb_mae": max(errors), "alpha_max_error": max(alpha_errors),
                    "png_count": len(pngs), "clock_restored": True, "camera_restored": True, "passed": True})

summary = {"unity": smoke["unity"], "gpu": smoke["gpu"], "graphics_api": smoke["graphics_api"],
           "unity_checks": len(smoke["checks"]), "outputs": reports}
(root / "output_validation_summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
print(json.dumps(summary, indent=2))
