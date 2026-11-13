"""Reads any PCM or float WAV the sound libraries ship (16/24/32-bit, float, extensible, RF64), into
float32 numpy arrays. Library WAVs often carry chunks (PEAK, iXML, bext, ...) that stricter readers
reject; this walks the RIFF chunks itself and only looks at `fmt ` and `data`."""
import struct

import numpy as np


def read(path):
    """Returns (samples[frames, channels] float32 in -1..1, rate)."""
    with open(path, "rb") as f:
        head = f.read(12)
        if len(head) < 12 or head[8:12] != b"WAVE" or head[0:4] not in (b"RIFF", b"RF64"):
            raise ValueError(f"{path}: not a WAV")
        fmt = None
        ds64_data = None
        while True:
            chunk = f.read(8)
            if len(chunk) < 8:
                raise ValueError(f"{path}: no data chunk")
            cid, size = chunk[0:4], struct.unpack("<I", chunk[4:8])[0]
            start = f.tell()
            if cid == b"ds64":
                ds64_data = struct.unpack("<Q", f.read(min(size, 28))[8:16])[0]
            elif cid == b"fmt ":
                body = f.read(size)
                tag, channels, rate, _, align, bits = struct.unpack("<HHIIHH", body[:16])
                if tag == 0xFFFE and len(body) >= 26:
                    tag = struct.unpack("<H", body[24:26])[0]
                fmt = (tag, channels, rate, bits, align)
            elif cid == b"data":
                if size == 0xFFFFFFFF and ds64_data is not None:
                    size = ds64_data
                raw = f.read(size)
                break
            f.seek(start + size + (size & 1))
    if fmt is None:
        raise ValueError(f"{path}: no fmt chunk")
    tag, channels, rate, bits, align = fmt
    frames = len(raw) // align
    raw = raw[: frames * align]
    if tag == 3 and bits == 32:
        data = np.frombuffer(raw, dtype="<f4").astype(np.float32)
    elif tag == 3 and bits == 64:
        data = np.frombuffer(raw, dtype="<f8").astype(np.float32)
    elif bits == 16:
        data = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    elif bits == 24:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        value = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        value = np.where(value >= 1 << 23, value - (1 << 24), value)
        data = value.astype(np.float32) / float(1 << 23)
    elif bits == 32:
        data = np.frombuffer(raw, dtype="<i4").astype(np.float32) / float(1 << 31)
    elif bits == 8:
        data = (np.frombuffer(raw, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    else:
        raise ValueError(f"{path}: {bits}-bit format {tag} not supported")
    return data.reshape(-1, channels), rate
