#!/usr/bin/env python
"""The PUBLISHER'S OWN generation loop, run through onnxruntime, so the managed driver can be held to it.

WHAT IT IS.  generate(), sample_top_p_k() and softmax() below are LIFTED from the SkyTNT midi-model
repository's app_onnx.py (Apache-2.0, commit f504d5cb58f769ab0f2909c679238f6621034573), and the prompt
construction is lifted from the same file's run().  They are copied rather than imported because
app_onnx.py imports gradio, requests and a synthesizer binding at module scope, none of which this
machine has and none of which the generation path needs.  The tokenizer and the MIDI writer are IMPORTED
from the clone as they stand.

WHAT IS NOT THE PUBLISHER'S.  This file's command line, its JSON output, and two deliberate departures,
both recorded in the report for phase O3:

  * io_binding is replaced by a plain sess.run with the same feeds.  The arithmetic is identical; binding
    only avoids copies.
  * a MIDI prompt has the tokenizer's END-OF-PIECE row taken off it before the model is asked to continue
    from it, which is what the managed driver does.  Left on, the model is being shown "and that is the
    end of the piece" and asked to write more.

NOTHING IS INSTALLED TO RUN IT.  midi_tokenizer.py imports PIL at module scope and uses it only in
midi2img, which the generation path never calls, so a six-line stub stands in for PIL.

THE CLONE IS NOT IN THIS REPOSITORY and never will be: the test that runs this script is gated on an
environment variable naming a checkout of it.
"""
import argparse
import json
import os
import sys
import time
import types

import numpy as np
import onnxruntime as rt


def install_pil_stub():
    """midi_tokenizer.py's only unsatisfiable import; the generation path never touches it."""
    if "PIL" in sys.modules:
        return False
    try:
        import PIL.Image  # noqa: F401
        return False
    except ImportError:
        pil = types.ModuleType("PIL")
        image = types.ModuleType("PIL.Image")

        def _missing(*args, **kwargs):
            raise RuntimeError("PIL is not installed; only midi2img needs it")

        image.fromarray = _missing
        pil.Image = image
        sys.modules["PIL"] = pil
        sys.modules["PIL.Image"] = image
        return True


# ------------------------------------------------ lifted from app_onnx.py (Apache-2.0, f504d5cb)

def softmax(x, axis):
    x_max = np.amax(x, axis=axis, keepdims=True)
    exp_x_shifted = np.exp(x - x_max)
    return exp_x_shifted / np.sum(exp_x_shifted, axis=axis, keepdims=True)


def sample_top_p_k(probs, p, k, generator=None):
    if generator is None:
        generator = np.random
    probs_idx = np.argsort(-probs, axis=-1)
    probs_sort = np.take_along_axis(probs, probs_idx, -1)
    probs_sum = np.cumsum(probs_sort, axis=-1)
    mask = probs_sum - probs_sort > p
    probs_sort[mask] = 0.0
    mask = np.zeros(probs_sort.shape[-1])
    mask[:k] = 1
    probs_sort = probs_sort * mask
    probs_sort /= np.sum(probs_sort, axis=-1, keepdims=True)
    shape = probs_sort.shape
    probs_sort_flat = probs_sort.reshape(-1, shape[-1])
    probs_idx_flat = probs_idx.reshape(-1, shape[-1])
    next_token = np.stack([generator.choice(idxs, p=pvals)
                           for pvals, idxs in zip(probs_sort_flat, probs_idx_flat)])
    next_token = next_token.reshape(*shape[:-1])
    return next_token


def generate(model, tokenizer, prompt=None, batch_size=1, max_len=512, temp=1.0, top_p=0.98, top_k=20,
             disable_patch_change=False, disable_control_change=False, disable_channels=None,
             generator=None, stats=None):
    """app_onnx.generate() with io_binding replaced by plain sess.run (same arithmetic)."""
    if disable_channels is not None:
        disable_channels = [tokenizer.parameter_ids["channel"][c] for c in disable_channels]
    else:
        disable_channels = []
    if generator is None:
        generator = np.random
    max_token_seq = tokenizer.max_token_seq
    if prompt is None:
        input_tensor = np.full((1, max_token_seq), tokenizer.pad_id, dtype=np.int64)
        input_tensor[0, 0] = tokenizer.bos_id
        input_tensor = input_tensor[None, :, :]
        input_tensor = np.repeat(input_tensor, repeats=batch_size, axis=0)
    else:
        input_tensor = prompt
    input_tensor = input_tensor[:, -4096:]
    cur_len = input_tensor.shape[1]
    emb_size = 1024
    for output in model[0].get_outputs():
        if output.name == "hidden":
            emb_size = output.shape[2]
    past_len = 0
    base_past = {i.name: np.zeros((batch_size, i.shape[1], 0, i.shape[3]), dtype=np.float32)
                 for i in model[0].get_inputs() if i.name.startswith("past_key_values")}
    while cur_len < max_len:
        end = [False] * batch_size
        feeds = {"x": input_tensor[:, past_len:]}
        feeds.update(base_past)
        t0 = time.perf_counter()
        outs = model[0].run(None, feeds)
        base_ms = (time.perf_counter() - t0) * 1000.0
        names = [o.name for o in model[0].get_outputs()]
        omap = dict(zip(names, outs))
        for name in base_past:
            base_past[name] = omap[name.replace("past_key_values", "present")]
        hidden = omap["hidden"][:, -1:]

        next_token_seq = np.zeros((batch_size, 0), dtype=np.int64)
        event_names = [""] * batch_size
        tok_past = {i.name: np.zeros((batch_size, i.shape[1], 0, i.shape[3]), dtype=np.float32)
                    for i in model[1].get_inputs() if i.name.startswith("past_key_values")}
        tok_names = [o.name for o in model[1].get_outputs()]
        token_steps = 0
        token_ms = 0.0
        for i in range(max_token_seq):
            mask = np.zeros((batch_size, tokenizer.vocab_size), dtype=np.int64)
            for b in range(batch_size):
                if end[b]:
                    mask[b, tokenizer.pad_id] = 1
                    continue
                if i == 0:
                    mask_ids = list(tokenizer.event_ids.values()) + [tokenizer.eos_id]
                    if disable_patch_change:
                        mask_ids.remove(tokenizer.event_ids["patch_change"])
                    if disable_control_change:
                        mask_ids.remove(tokenizer.event_ids["control_change"])
                    mask[b, mask_ids] = 1
                else:
                    param_names = tokenizer.events[event_names[b]]
                    if i > len(param_names):
                        mask[b, tokenizer.pad_id] = 1
                        continue
                    param_name = param_names[i - 1]
                    mask_ids = tokenizer.parameter_ids[param_name]
                    if param_name == "channel":
                        mask_ids = [j for j in mask_ids if j not in disable_channels]
                    mask[b, mask_ids] = 1
            mask = mask[:, None, :]
            x = next_token_seq
            if i != 0:
                if i == 1:
                    hidden = np.zeros((batch_size, 0, emb_size), dtype=np.float32)
                x = x[:, -1:]
            feeds = {"hidden": hidden, "x": x}
            feeds.update(tok_past)
            t0 = time.perf_counter()
            outs = model[1].run(None, feeds)
            token_ms += (time.perf_counter() - t0) * 1000.0
            token_steps += 1
            omap = dict(zip(tok_names, outs))
            for name in tok_past:
                tok_past[name] = omap[name.replace("past_key_values", "present")]
            logits = omap["y"]
            scores = softmax(logits / temp, -1) * mask
            samples = sample_top_p_k(scores, top_p, top_k, generator)
            if i == 0:
                next_token_seq = samples
                for b in range(batch_size):
                    if end[b]:
                        continue
                    eid = samples[b].item()
                    if eid == tokenizer.eos_id:
                        end[b] = True
                    else:
                        event_names[b] = tokenizer.id_events[eid]
            else:
                next_token_seq = np.concatenate([next_token_seq, samples], axis=1)
                if all([len(tokenizer.events[event_names[b]]) == i
                        for b in range(batch_size) if not end[b]]):
                    break
        if next_token_seq.shape[1] < max_token_seq:
            next_token_seq = np.pad(next_token_seq,
                                    ((0, 0), (0, max_token_seq - next_token_seq.shape[-1])),
                                    mode="constant", constant_values=tokenizer.pad_id)
        next_token_seq = next_token_seq[:, None, :]
        input_tensor = np.concatenate([input_tensor, next_token_seq], axis=1)
        past_len = cur_len
        cur_len += 1
        if stats is not None:
            stats.append({"base_ms": base_ms, "token_ms": token_ms, "token_steps": token_steps})
        yield next_token_seq[:, 0]
        if all(end):
            break


def build_prompt(tokenizer, settings):
    """app_onnx.run()'s prompt construction, with numbers where the web page had names."""
    mid = [[tokenizer.bos_id] + [tokenizer.pad_id] * (tokenizer.max_token_seq - 1)]
    time_sig = settings.get("time_signature")
    key_sig = settings.get("key_signature")
    bpm = settings.get("bpm") or 0
    if tokenizer.version == "v2":
        if time_sig is not None:
            # The settings name a time signature the way it is written - 3 and 4 for 3/4 - and the model
            # holds the lower number as the POWER OF TWO it is, one less again. It is what run() does with
            # its {2: 1, 4: 2, 8: 3} table, written out for any power of two.
            power = time_sig[1].bit_length() - 1
            mid.append(tokenizer.event2tokens(
                ["time_signature", 0, 0, 0, time_sig[0] - 1, power - 1]))
        if key_sig is not None:
            mid.append(tokenizer.event2tokens(
                ["key_signature", 0, 0, 0, key_sig[0] + 7, key_sig[1]]))
    if bpm != 0:
        mid.append(tokenizer.event2tokens(["set_tempo", 0, 0, 0, bpm]))
    patches = {}
    i = 0
    for instr in settings.get("instruments") or []:
        patches[i] = instr
        i = (i + 1) if i != 8 else 10
    if settings.get("drum_kit") is not None:
        patches[9] = settings["drum_kit"]
    for i, (c, p) in enumerate(patches.items()):
        mid.append(tokenizer.event2tokens(["patch_change", 0, 0, i + 1, c, p]))
    disable_patch_change = len(settings.get("instruments") or []) > 0
    disable_channels = [i for i in range(16) if i not in patches] if disable_patch_change else None
    return mid, disable_patch_change, disable_channels


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clone", required=True, help="a checkout of the publisher's repository")
    ap.add_argument("--config", required=True)
    ap.add_argument("--base", required=True)
    ap.add_argument("--token", required=True)
    ap.add_argument("--work", required=True)
    ap.add_argument("--events", type=int, default=64)
    ap.add_argument("--threads", type=int, default=0)
    ap.add_argument("--settings", default="{}", help="JSON: instruments, drum_kit, bpm, time_signature, key_signature, allow_cc")
    ap.add_argument("--prompt-midi", default=None)
    args = ap.parse_args()

    stubbed = install_pil_stub()
    sys.path.insert(0, args.clone)
    import MIDI  # noqa: E402
    from midi_tokenizer import MIDITokenizer  # noqa: E402

    settings = json.loads(args.settings)
    config = json.load(open(args.config))
    tconf = config["tokenizer"]
    tokenizer = MIDITokenizer(tconf["version"])
    tokenizer.set_optimise_midi(tconf["optimise_midi"])
    assert tokenizer.vocab_size == tconf["vocab_size"], "the port and the bundle disagree on the vocabulary"

    so = rt.SessionOptions()
    if args.threads:
        so.intra_op_num_threads = args.threads
    so.graph_optimization_level = rt.GraphOptimizationLevel.ORT_ENABLE_ALL
    base = rt.InferenceSession(args.base, so, providers=["CPUExecutionProvider"])
    token = rt.InferenceSession(args.token, so, providers=["CPUExecutionProvider"])

    if args.prompt_midi:
        with open(args.prompt_midi, "rb") as f:
            raw = f.read()
        eps = 4 if settings.get("reduce_repeated", True) else 0
        mid = tokenizer.tokenize(MIDI.midi2score(raw), cc_eps=eps, tempo_eps=eps)
        if len(mid) and mid[-1][0] == tokenizer.eos_id:
            mid = mid[:-1]
        limit = settings.get("prompt_event_limit", 4096)
        mid = mid[:limit]
        disable_patch_change = False
        disable_channels = None
    else:
        mid, disable_patch_change, disable_channels = build_prompt(tokenizer, settings)

    prompt = np.asarray([mid], dtype=np.int64)
    max_len = args.events + prompt.shape[1]

    os.makedirs(args.work, exist_ok=True)
    rng = np.random.RandomState(20260918)
    stats = []
    rows = []
    mid_seq = [list(r) for r in mid]
    t0 = time.perf_counter()
    for seq in generate((base, token), tokenizer, prompt=prompt, max_len=max_len,
                        temp=settings.get("temperature", 1.0), top_p=settings.get("top_p", 1.0),
                        top_k=settings.get("top_k", 1),
                        disable_patch_change=disable_patch_change,
                        disable_control_change=not settings.get("allow_cc", True),
                        disable_channels=disable_channels, generator=rng, stats=stats):
        tokens = seq[0].tolist()
        mid_seq.append(tokens)
        rows.append({"tokens": tokens, "event": tokenizer.tokens2event(tokens)})
    elapsed = time.perf_counter() - t0

    score = tokenizer.detokenize(mid_seq)
    with open(os.path.join(args.work, "generated.mid"), "wb") as f:
        f.write(MIDI.score2midi(score))

    prompt_beats = 0
    for row in mid:
        event = tokenizer.tokens2event(list(row))
        if event:
            prompt_beats += event[1]

    report = {
        "pil_stub": stubbed,
        "prompt_beats": prompt_beats,
        "vocab_size": tokenizer.vocab_size,
        "max_token_seq": tokenizer.max_token_seq,
        "prompt_rows": [list(r) for r in mid],
        "events": rows,
        "score": [score[0]] + [[list(e) for e in track] for track in score[1:]],
        "per_event_ms": stats,
        "elapsed_s": elapsed,
    }
    with open(os.path.join(args.work, "oracle.json"), "w") as f:
        json.dump(report, f)

    print(f"events={len(rows)} elapsed={elapsed:.2f}s => {len(rows) / max(elapsed, 1e-9):.2f} events/s")
    print("SKYTNT ORACLE OK")


if __name__ == "__main__":
    main()
