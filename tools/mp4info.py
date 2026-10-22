"""Prints the tracks of an MP4: handler type, codec fourcc, duration. No dependencies.

Usage: python -I tools/mp4info.py <file.mp4>
"""
import struct
import sys

CONTAINERS = {b"moov", b"trak", b"mdia", b"minf", b"stbl", b"moof", b"traf", b"mvex"}


def boxes(data, start, end):
    pos = start
    while pos + 8 <= end:
        size, kind = struct.unpack(">I4s", data[pos:pos + 8])
        header = 8
        if size == 1:
            size = struct.unpack(">Q", data[pos + 8:pos + 16])[0]
            header = 16
        elif size == 0:
            size = end - pos
        if size < header:
            return
        yield kind, pos + header, pos + size
        pos += size


def find(data, start, end, path):
    for kind, s, e in boxes(data, start, end):
        if kind == path[0]:
            if len(path) == 1:
                return s, e
            return find(data, s, e, path[1:])
    return None


def main(path):
    data = open(path, "rb").read()
    moov = find(data, 0, len(data), [b"moov"])
    if not moov:
        print("no moov box")
        return 1
    mvhd = find(data, *moov, [b"mvhd"])
    if mvhd:
        v = data[mvhd[0]]
        if v == 1:
            scale, dur = struct.unpack(">IQ", data[mvhd[0] + 20:mvhd[0] + 32])
        else:
            scale, dur = struct.unpack(">II", data[mvhd[0] + 12:mvhd[0] + 20])
        print(f"movie duration: {dur / scale if scale else 0:.2f} s")
    fragments = sum(1 for k, _, _ in boxes(data, 0, len(data)) if k == b"moof")
    if fragments:
        print(f"fragments: {fragments}")
    for kind, s, e in boxes(data, *moov):
        if kind != b"trak":
            continue
        mdhd = find(data, s, e, [b"mdia", b"mdhd"])
        hdlr = find(data, s, e, [b"mdia", b"hdlr"])
        stsd = find(data, s, e, [b"mdia", b"minf", b"stbl", b"stsd"])
        handler = data[hdlr[0] + 8:hdlr[0] + 12].decode() if hdlr else "?"
        codec = data[stsd[0] + 12:stsd[0] + 16].decode(errors="replace") if stsd else "?"
        seconds = 0.0
        if mdhd:
            v = data[mdhd[0]]
            if v == 1:
                scale, dur = struct.unpack(">IQ", data[mdhd[0] + 20:mdhd[0] + 32])
            else:
                scale, dur = struct.unpack(">II", data[mdhd[0] + 12:mdhd[0] + 20])
            seconds = dur / scale if scale else 0
        extra = ""
        if handler == "vide" and stsd:
            w, h = struct.unpack(">HH", data[stsd[0] + 8 + 32:stsd[0] + 8 + 36])
            extra = f" {w}x{h}"
        print(f"track: {handler} {codec}{extra} {seconds:.2f} s")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1]))
