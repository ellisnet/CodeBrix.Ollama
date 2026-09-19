#!/usr/bin/env python
"""Writes THE SAMPLING MASKS the publisher's own generation loop uses, so the port can be held to them.

WHAT A MASK IS.  At every position of an event's row of tokens the model is allowed to answer with only
some of the vocabulary: the first token may say which of the six kinds of event it is or that the piece
is finished, and each token after it may only be a value of the one parameter that kind puts there.  Get
one of those wrong and the model still runs, still produces rows, and writes music that is subtly not
the music the publisher's own code would have written - which is why they are pinned against the
publisher's own tokenizer rather than against this port's idea of itself.

HOW IT IS MADE.  The branches below are the mask-building half of app_onnx.py's generate() (Apache-2.0,
SkyTNT/midi-model @ f504d5cb58f769ab0f2909c679238f6621034573), evaluated over every kind of event, every
position, and the refusals the loop supports, against the tokenizer IMPORTED from a checkout of that
repository.  Run by hand, once:

    <venv>/bin/python generate_mask_fixtures.py --clone <a checkout of the publisher's repository>

midi_tokenizer.py imports PIL at module scope and uses it only in midi2img, so a stub stands in for it;
nothing is installed.

WHAT IS OURS.  masks.json is a table of token numbers this script computed, and it carries this
repository's licence (MIT).  The VOCABULARY those numbers describe is the publisher's, under the
Apache-2.0 licence recorded in THIRD-PARTY-NOTICES.txt.  No file of the publisher's is checked in.

THE TEST SUITE NEVER RUNS THIS.  It reads masks.json as it is checked in.
"""
import argparse
import json
import os
import sys
import types

HERE = os.path.dirname(os.path.abspath(__file__))


def install_pil_stub():
    if "PIL" in sys.modules:
        return False
    try:
        import PIL.Image  # noqa: F401
        return False
    except ImportError:
        pil = types.ModuleType("PIL")
        image = types.ModuleType("PIL.Image")
        pil.Image = image
        sys.modules["PIL"] = pil
        sys.modules["PIL.Image"] = image
        return True


def mask_ids(tokenizer, position, ended, event_name, disable_patch_change, disable_control_change,
             disabled_channels):
    """The branches of app_onnx.generate()'s mask, for one batch row."""
    if ended:
        return [tokenizer.pad_id]
    if position == 0:
        ids = list(tokenizer.event_ids.values()) + [tokenizer.eos_id]
        if disable_patch_change:
            ids.remove(tokenizer.event_ids["patch_change"])
        if disable_control_change:
            ids.remove(tokenizer.event_ids["control_change"])
        return sorted(ids)
    param_names = tokenizer.events[event_name]
    if position > len(param_names):
        return [tokenizer.pad_id]
    param_name = param_names[position - 1]
    ids = tokenizer.parameter_ids[param_name]
    if param_name == "channel":
        blocked = [tokenizer.parameter_ids["channel"][c] for c in disabled_channels]
        ids = [j for j in ids if j not in blocked]
    return sorted(ids)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clone", required=True, help="a checkout of the publisher's repository")
    args = ap.parse_args()

    install_pil_stub()
    sys.path.insert(0, args.clone)
    from midi_tokenizer import MIDITokenizer  # noqa: E402

    tokenizer = MIDITokenizer("v2")
    tokenizer.set_optimise_midi(True)

    cases = []

    def case(name, position, ended=False, event=None, disable_patch_change=False,
             disable_control_change=False, disabled_channels=()):
        cases.append({
            "name": name,
            "position": position,
            "ended": ended,
            "event": event,
            "disable_patch_change": disable_patch_change,
            "disable_control_change": disable_control_change,
            "disabled_channels": list(disabled_channels),
            "allowed": mask_ids(tokenizer, position, ended, event, disable_patch_change,
                                disable_control_change, list(disabled_channels)),
        })

    case("first_token", 0)
    case("first_token_no_patch_change", 0, disable_patch_change=True)
    case("first_token_no_control_change", 0, disable_control_change=True)
    case("first_token_no_patch_or_control_change", 0, disable_patch_change=True,
         disable_control_change=True)
    case("after_the_end", 1, ended=True)

    for event_name, params in tokenizer.events.items():
        for position in range(1, len(params) + 1):
            case(f"{event_name}_{position}_{params[position - 1]}", position, event=event_name)

        # One position past the last parameter, which the loop reaches only when several pieces are
        # written at once and one of them has a longer row than another.
        case(f"{event_name}_beyond", len(params) + 1, event=event_name)

    # Confined to four channels, which is what asking for three instruments and a drum kit does.
    kept = [0, 1, 2, 9]
    blocked = [c for c in range(16) if c not in kept]
    for event_name in ("note", "patch_change", "control_change"):
        position = tokenizer.events[event_name].index("channel") + 1
        case(f"{event_name}_channel_only_0_1_2_9", position, event=event_name,
             disabled_channels=blocked)

    report = {
        "vocab_size": tokenizer.vocab_size,
        "max_token_seq": tokenizer.max_token_seq,
        "pad_id": tokenizer.pad_id,
        "bos_id": tokenizer.bos_id,
        "eos_id": tokenizer.eos_id,
        "event_ids": {name: token for name, token in tokenizer.event_ids.items()},
        "parameter_first_ids": {name: ids[0] for name, ids in tokenizer.parameter_ids.items()},
        "parameter_sizes": {name: len(ids) for name, ids in tokenizer.parameter_ids.items()},
        "cases": cases,
    }

    path = os.path.join(HERE, "masks.json")
    with open(path, "w") as f:
        json.dump(report, f, indent=1, sort_keys=False)
        f.write("\n")
    print(f"wrote {path}: {len(cases)} masks, {os.path.getsize(path)} bytes")


if __name__ == "__main__":
    main()
