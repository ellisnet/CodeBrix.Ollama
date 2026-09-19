THE MIDI-DRIVER FIXTURES
========================
Everything the MIDI generation driver is tested against WITH NOTHING
INSTALLED: a model small enough to check in, and the sampling masks the
publisher's own code uses.

WHAT IS HERE
    tiny-model/config.json          a bundle that describes itself exactly as the
                                    published one does - the same architecture
                                    name, the same tokenizer version, the same
                                    3406 tokens and the same eight-token rows
    tiny-model/onnx/model_base.onnx   1.8 KB: the graph that turns the events so
                                    far into one state per event
    tiny-model/onnx/model_token.onnx  68 KB: the graph that turns one state into
                                    an event's tokens, one at a time
    masks.json                      which tokens the model may answer with at
                                    every position of every kind of event, and
                                    under every refusal the loop supports
    generate_tiny_model.py          makes the bundle
    generate_mask_fixtures.py       makes masks.json

WHY A MODEL OF OUR OWN
    The published model is a gigabyte and cannot be checked in, and a test that
    needed it would be a test that only runs on a machine that has downloaded
    it. The tiny model has the SAME CONTRACT - the same input and output names,
    the same two caches, the same shapes - and arithmetic the driver cannot tell
    from a real model's, so the loop, both caches, the masks and the stop
    conditions are all exercised offline. It is not a music model: what it
    writes is not music and is not meant to be.

    It stops by itself. One number of every state it produces is the LENGTH of
    the base graph's cache, and the ending token's answer is the only thing that
    reads it - so the longer the piece, the more the model wants to finish, and
    a greedy generation ends after about a dozen events however many were asked
    for. That is also the fence for the cache: a driver that failed to feed the
    cache back would never stop.

WHO OWNS THEM
    THEY ARE OURS, and they carry this repository's licence (MIT). The weights
    are numbers the script makes up from a fixed seed; masks.json is a table of
    token numbers the other script computed. No publisher's model, no
    publisher's file and no downloaded file is among them.

    The VOCABULARY those token numbers describe is the publisher's, and so is
    the shape of the two graphs' contract. Both are recorded in
    THIRD-PARTY-NOTICES.txt at the root, under the Apache-2.0 licence of the
    repository they come from.

HOW THEY WERE MADE
    By hand, once, in a virtual environment holding onnx and numpy:

        <venv>/bin/python generate_tiny_model.py
        <venv>/bin/python generate_mask_fixtures.py --clone <publisher's repository>

    The second one needs a checkout of the publisher's repository, which is not
    in this repository and never will be. THE TEST SUITE NEVER RUNS EITHER
    SCRIPT: the tests read these files as they are checked in, which is the
    whole point - what they compare against was computed somewhere else.
