#!/usr/bin/env python
"""Reads a Standard MIDI File with a parser that is NOT ours, and says what is in it.

The point is a SECOND OPINION on the files this library writes. The parser is MIDI.py out of a checkout
of the MIDI model publisher's repository - the same one the publisher's own code writes its files with -
so a file this library produced is read by something that had no part in producing it. Nothing is
installed: the checkout is named on the command line and is never part of this repository.
"""
import argparse
import json
import os
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clone", required=True, help="a checkout of the publisher's repository")
    ap.add_argument("--file", required=True)
    args = ap.parse_args()

    sys.path.insert(0, args.clone)
    import MIDI  # noqa: E402

    with open(args.file, "rb") as f:
        raw = f.read()

    score = MIDI.midi2score(raw)
    ticks = score[0]
    tracks = score[1:]
    counts = {}
    length = 0
    for track in tracks:
        for event in track:
            counts[event[0]] = counts.get(event[0], 0) + 1
            if event[0] == "note":
                length = max(length, event[1] + event[2])
            else:
                length = max(length, event[1])

    print(json.dumps({
        "bytes": os.path.getsize(args.file),
        "ticks_per_beat": ticks,
        "tracks": len(tracks),
        "events": counts,
        "length_ticks": length,
    }))
    print("MIDI PARSE OK")


if __name__ == "__main__":
    main()
