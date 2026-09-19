#!/usr/bin/env python
# The SECOND OPINION for the end-to-end tests: what the checkpoint's own framework tokenizes and generates.
#
# It is run by the test project as a child process of the virtual environment's interpreter, never by either
# library and never by a build. It loads the checkpoint that was laid out for it, encodes the probe strings,
# and greedily continues each prompt for a fixed number of tokens, writing the identifiers it produced to a
# JSON file. The test compares those identifiers with the ones the converted GGUF model produces.
#
#     python torch_oracle.py --checkpoint <dir> --request <in.json> --output <out.json> --dtype bfloat16
#
# The request file is {"probes": [...], "prompts": [...], "maxNewTokens": 32}; the output file is
# {"dtype": ..., "loadSeconds": ..., "tokenization": [[ids], ...], "greedy": [[ids], ...]}, in the order the
# request listed them.
#
# WHY GREEDY, AND WHY NO END-OF-SEQUENCE. Sampling would compare two random processes, so the temperature is
# zero on both sides. eos_token_id is set to None so that the reference always produces the full number of
# tokens asked for: a run that stopped early would compare a short list with a long one and say nothing about
# whether the two models agree.
#
# WHY THE DTYPE IS AN ARGUMENT. A GGUF file written at BF16 holds bfloat16 weights and is compared with the
# checkpoint READ as bfloat16; one written at F16 keeps more of the significand than bfloat16 does and is
# compared with float32. Comparing either against the other precision proves nothing: the two disagree at the
# first near-tie, which is arithmetic rather than conversion.
#
# Everything here is ours and is MIT licensed with the repository.

import argparse
import json
import time

import torch
from transformers import AutoModelForCausalLM, AutoTokenizer

DTYPES = {"bfloat16": torch.bfloat16, "float16": torch.float16, "float32": torch.float32}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", required=True, help="the checkpoint folder to read")
    parser.add_argument("--request", required=True, help="the JSON file naming the probes and the prompts")
    parser.add_argument("--output", required=True, help="the JSON file to write")
    parser.add_argument("--dtype", required=True, choices=sorted(DTYPES), help="the precision to read at")
    arguments = parser.parse_args()

    with open(arguments.request, "r", encoding="utf-8") as f:
        request = json.load(f)

    probes = request.get("probes", [])
    prompts = request.get("prompts", [])
    max_new_tokens = int(request.get("maxNewTokens", 32))

    tokenizer = AutoTokenizer.from_pretrained(
        arguments.checkpoint, trust_remote_code=True, use_fast=False)
    tokenization = [tokenizer.encode(probe) for probe in probes]

    started = time.time()
    model = AutoModelForCausalLM.from_pretrained(
        arguments.checkpoint, trust_remote_code=True, dtype=DTYPES[arguments.dtype]).eval()
    load_seconds = time.time() - started

    greedy = []
    for prompt in prompts:
        ids = torch.tensor([tokenizer.encode(prompt)])
        with torch.no_grad():
            generated = model.generate(
                ids,
                max_new_tokens=max_new_tokens,
                do_sample=False,
                num_beams=1,
                pad_token_id=0,
                eos_token_id=None)
        greedy.append(generated[0][ids.shape[1]:].tolist())

    result = {
        "dtype": arguments.dtype,
        "loadSeconds": round(load_seconds, 3),
        "tokenization": tokenization,
        "greedy": greedy,
    }
    with open(arguments.output, "w", encoding="utf-8") as f:
        json.dump(result, f)

    print("wrote " + arguments.output + " (" + arguments.dtype + ", load "
          + format(load_seconds, ".2f") + " s)", flush=True)


if __name__ == "__main__":
    main()
