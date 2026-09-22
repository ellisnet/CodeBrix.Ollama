# Inference-only exporters for Microsoft Muzic MuseCoco (MIT).
# Equations and vocabulary contract: microsoft/muzic, musecoco, commit
# 2b8739671ba06f819f31f568b8a79da581aaf6f9. Sinusoidal positions follow fairseq
# (MIT), commit 83e615d66905b8ca7483122a37da1a85f13f4b8e. See THIRD-PARTY-NOTICES.
# The caller supplies input_path, output_path, model_kind, precision,
# attribute_schema_json and music_vocabulary_json. Never imports checkpoint code,
# fairseq, transformers, fast-transformers, or a custom native attention kernel.
import argparse
import json
import math
import os

import numpy as np
import onnx
from onnx import helper, numpy_helper, TensorProto
from onnx.external_data_helper import set_external_data
import torch


class GraphWriter:
    def __init__(self, output, state):
        self.output = output
        self.state = state
        self.used = set()
        self.nodes, self.tensors = [], []
        self.inputs, self.outputs = [], []
        self.writer = open(os.path.join(output, "weights.bin"), "wb")
        self.serial = 0
        self.written = {}
        self.epsilon = 1e-5
        self.constant("one", 1)
        self.constant("half", 0.5)
        self.constant("sqrt_two", math.sqrt(2))

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_value, traceback):
        self.writer.close()

    def node(self, op, args, name=None, **attrs):
        self.serial += 1
        name = name or "v" + str(self.serial)
        self.nodes.append(helper.make_node(op, args, [name], name=name, **attrs))
        return name

    def constant(self, name, value, dtype=np.float32):
        if name == "norm_epsilon":
            self.epsilon = float(value)
            return name
        self.tensors.append(numpy_helper.from_array(np.asarray(value, dtype=dtype), name))
        return name

    def weight(self, key, transpose=False, value=None):
        name = key + (".transposed" if transpose else "")
        if name in self.written:
            return name
        if value is None:
            value = self.state[key]
            self.used.add(key)
        if value.dtype != torch.float32:
            raise ValueError("Expected FP32 checkpoint tensor: " + key)
        if transpose:
            value = value.t()
        array = np.ascontiguousarray(value.detach().cpu().numpy())
        offset = self.writer.tell()
        self.writer.write(memoryview(array).cast("B"))
        tensor = TensorProto()
        tensor.name, tensor.data_type = name, TensorProto.FLOAT
        tensor.dims.extend(array.shape)
        tensor.raw_data = b"external"
        set_external_data(tensor, location="weights.bin", offset=offset, length=array.nbytes)
        tensor.ClearField("raw_data")
        self.tensors.append(tensor)
        self.written[name] = True
        return name

    def linear(self, x, key, bias=True):
        # Stable descriptive node names are also the public quantizer exclusion names.
        y = self.node("MatMul", [x, self.weight(key + ".weight", True)], key + ".MatMul")
        return self.node("Add", [y, self.weight(key + ".bias")]) if bias else y

    def norm(self, x, key):
        return self.node("LayerNormalization", [x, self.weight(key + ".weight"), self.weight(key + ".bias")],
                         key + ".LayerNormalization", axis=-1, epsilon=self.epsilon, stash_type=1)

    def gelu(self, x):
        cdf = self.node("Add", [self.node("Erf", [self.node("Div", [x, "sqrt_two"])]), "one"])
        return self.node("Mul", [self.node("Mul", [x, "half"]), cdf])

    def info(self, name, shape, integer=False):
        return helper.make_tensor_value_info(name, TensorProto.INT64 if integer else TensorProto.FLOAT, shape)

    def finish(self, filename, ignored):
        extra = set(self.state) - self.used - set(ignored)
        if extra:
            raise ValueError("Unsupported checkpoint tensors: " + ", ".join(sorted(extra)))
        self.writer.close()
        graph = helper.make_graph(self.nodes, "MuseCoco", self.inputs, self.outputs, self.tensors)
        model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 17)],
                                  producer_name="CodeBrix.Ollama.ModelManager", ir_version=9)
        path = os.path.join(self.output, filename)
        onnx.save_model(model, path)
        onnx.checker.check_model(path)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def write_json(name, value):
    with open(os.path.join(output_path, name), "w", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, ensure_ascii=False)
        stream.write("\n")


def export_music(schema, vocabulary):
    checkpoints = [name for name in os.listdir(input_path) if name.endswith(".pt")]
    require(len(checkpoints) == 1, "MuseCocoMusic requires exactly one .pt checkpoint at the bundle root")
    # The only additional pickle global is argparse's plain Namespace. Arbitrary
    # pickle code and publisher modules remain forbidden by weights_only=True.
    with torch.serialization.safe_globals([argparse.Namespace]):
        checkpoint = torch.load(os.path.join(input_path, checkpoints[0]), weights_only=True,
                                mmap=True, map_location="cpu")
    args, state = checkpoint["args"], checkpoint["model"]
    require(isinstance(args, argparse.Namespace), "Expected fairseq argument metadata")
    for key in ("decoder_learned_pos", "no_token_positional_embeddings", "character_embeddings",
                "adaptive_input", "layernorm_embedding", "no_decoder_final_norm"):
        require(not getattr(args, key, False), "Unsupported music configuration: " + key)
    require(args.decoder_normalize_before and args.activation_fn == "gelu", "Expected pre-norm GELU decoder")
    require(not args.adaptive_softmax_cutoff and not args.quant_noise_pq, "Adaptive softmax/quantization noise is unsupported")
    hidden, heads, layers = args.decoder_embed_dim, args.decoder_attention_heads, args.decoder_layers
    require(0 < heads <= hidden and hidden >= 4 and layers > 0, "Invalid music dimensions")
    require(args.decoder_input_dim == hidden and args.decoder_output_dim == hidden, "Input/output projections are unsupported")
    width = hidden // heads  # The published model intentionally projects 2048 to 24 * 85.
    require(tuple(state["decoder.embed_tokens.weight"].shape) == (len(vocabulary), hidden), "Music vocabulary/embedding mismatch")
    with GraphWriter(output_path, state) as g:
        n = g.node
        g.inputs = [g.info("token", [1], True), g.info("position", [1], True)]
        for key, value in [("norm_epsilon", 1e-5), ("attention_epsilon", 1e-6),
                           ("embed_scale", 1 if getattr(args, "no_scale_embedding", False) else math.sqrt(hidden))]:
            g.constant(key, value)
        for key, value in [("head_shape", [heads, width]), ("query_shape", [heads, 1, width]),
                           ("key_shape", [heads, width, 1]), ("flat_shape", [1, heads * width]),
                           ("denominator_shape", [heads, 1, 1]), ("sum_axis", [1]), ("pad_token", [1])]:
            g.constant(key, value, np.int64)
        # The runtime enforces this table's bound. Position 0 suppresses positions in
        # the attribute prefix, and row 1 is fairseq's padding position.
        positions_count = 8194
        half = hidden // 2
        frequencies = torch.exp(torch.arange(half, dtype=torch.float32) * -(math.log(10000) / (half - 1)))
        phases = torch.arange(positions_count, dtype=torch.float32).unsqueeze(1) * frequencies.unsqueeze(0)
        positions = torch.cat([torch.sin(phases), torch.cos(phases)], dim=1)
        if hidden % 2:
            positions = torch.cat([positions, torch.zeros(positions_count, 1)], dim=1)
        positions[:2].zero_()
        x = n("Add", [n("Mul", [n("Gather", [g.weight("decoder.embed_tokens.weight"), "token"], axis=0), "embed_scale"]),
                      n("Gather", [g.weight("positions", value=positions), "position"], axis=0)])
        keep = n("Sub", ["one", n("Cast", [n("Equal", ["token", "pad_token"])], to=TensorProto.FLOAT)])
        for i in range(layers):
            p = "decoder.layers." + str(i)
            s_name, z_name = "state_s_" + str(i), "state_z_" + str(i)
            g.inputs.extend([g.info(s_name, [heads, width, width]), g.info(z_name, [heads, width])])
            residual = x
            y = g.norm(x, p + ".self_attn_layer_norm")
            projected = []
            for key in ("q", "k", "v"):
                require(tuple(state[p + ".self_attn." + key + "_proj.weight"].shape) == (heads * width, hidden), "Invalid attention projection")
                part = n("Reshape", [g.linear(y, p + ".self_attn." + key + "_proj"), "head_shape"])
                if key != "v":
                    part = n("Add", [n("Elu", [part], alpha=1.0), "one"])
                projected.append(part)
            q, k, v = projected
            k = n("Mul", [k, keep])
            s = n("Add", [s_name, n("Mul", [n("Reshape", [k, "key_shape"]), n("Reshape", [v, "query_shape"])])], "next_s_" + str(i))
            z = n("Add", [z_name, k], "next_z_" + str(i))
            numerator = n("MatMul", [n("Reshape", [q, "query_shape"]), s])
            denominator = n("Add", [n("ReduceSum", [n("Mul", [q, z]), "sum_axis"], keepdims=1), "attention_epsilon"])
            attended = n("Reshape", [n("Div", [numerator, n("Reshape", [denominator, "denominator_shape"])]), "flat_shape"])
            x = n("Add", [residual, g.linear(attended, p + ".self_attn.out_proj")])
            residual = x
            x = g.gelu(g.linear(g.norm(x, p + ".final_layer_norm"), p + ".fc1"))
            x = n("Add", [residual, g.linear(x, p + ".fc2")])
            g.outputs.extend([g.info(s, [heads, width, width]), g.info(z, [heads, width])])
        x = g.linear(g.norm(x, "decoder.layer_norm"), "decoder.output_projection", bias=False)
        n("Identity", [x], "logits")
        g.outputs.insert(0, g.info("logits", [1, len(vocabulary)]))
        g.finish("decoder.onnx", ["decoder.version", "decoder.embed_positions._float_tensor"])
        write_json("vocabulary.json", vocabulary)
        return dict(kind="music", graph="decoder.onnx", layers=layers, hiddenSize=hidden,
                    heads=heads, headSize=width, positionCount=positions_count, vocabulary="vocabulary.json",
                    ticksPerQuarterNote=480, positionsPerQuarterNote=12, prefixPositionStart=len(schema["definitions"]))


def export_text(schema):
    with open(os.path.join(input_path, "config.json"), encoding="utf-8") as stream:
        config = json.load(stream)
    require(config.get("architectures") == ["BertForAttributModel"], "Expected MuseCoco BertForAttributModel")
    require(config.get("hidden_act") == "gelu" and config.get("position_embedding_type", "absolute") == "absolute",
            "Expected BERT with absolute positions and GELU")
    require(not config.get("is_decoder", False) and not config.get("add_cross_attention", False), "Expected bidirectional BERT")
    with open(os.path.join(input_path, "vocab.txt"), encoding="utf-8") as stream:
        vocabulary = stream.read().splitlines()
    with open(os.path.join(input_path, "tokenizer_config.json"), encoding="utf-8") as stream:
        tokenizer = json.load(stream)
    require(tokenizer.get("tokenizer_class") in ("BertTokenizer", "BertTokenizerFast"), "Expected BERT WordPiece tokenizer")
    require(tokenizer.get("do_lower_case") is True and tokenizer.get("strip_accents") in (None, True)
            and tokenizer.get("tokenize_chinese_chars", True) is True, "Expected uncased BERT tokenizer normalization")
    require(len(vocabulary) == config["vocab_size"] and len(set(vocabulary)) == len(vocabulary), "Invalid BERT vocabulary")
    for key, expected in [("unk_token", "[UNK]"), ("sep_token", "[SEP]"), ("pad_token", "[PAD]"),
                          ("cls_token", "[CLS]"), ("mask_token", "[MASK]")]:
        require(tokenizer.get(key) == expected and expected in vocabulary, "Unsupported special token: " + key)
    state = torch.load(os.path.join(input_path, "pytorch_model.bin"), weights_only=True, mmap=True, map_location="cpu")
    hidden, heads, layers = config["hidden_size"], config["num_attention_heads"], config["num_hidden_layers"]
    require(hidden > 0 and heads > 0 and hidden % heads == 0 and layers > 0, "Invalid BERT dimensions")
    definitions = sorted((d for d in schema["definitions"] if d["bertHead"] >= 0), key=lambda d: d["bertHead"])
    count = len(definitions)
    ids = [vocabulary.index("[unused" + str(i) + "]") for i in range(count)]
    with GraphWriter(output_path, state) as g:
        n = g.node
        g.inputs = [g.info(key, [1, "sequence"], True) for key in ("input_ids", "attention_mask", "token_type_ids", "position_ids")]
        for key, value in [("norm_epsilon", config["layer_norm_eps"]), ("attention_scale", math.sqrt(hidden // heads)),
                           ("mask_value", np.finfo(np.float32).min), ("attribute_mask", np.ones((1, count), dtype=np.float32))]:
            g.constant(key, value)
        for key, value in [("cls_ids", [ids]), ("cls_types", [[0] * count]), ("heads_shape", [1, -1, heads, hidden // heads]),
                           ("merged_shape", [1, -1, hidden]), ("mask_shape", [1, 1, 1, -1]),
                           ("attribute_indices", list(range(count))), ("pooled_shape", [count, hidden])]:
            g.constant(key, value, np.int64)
        def embed(ids_name, table):
            return n("Gather", [g.weight("bert.embeddings." + table + ".weight"), ids_name], axis=0)
        prompt = n("Add", [n("Add", [embed("input_ids", "word_embeddings"), embed("token_type_ids", "token_type_embeddings")]),
                           embed("position_ids", "position_embeddings")])
        cls = n("Add", [embed("cls_ids", "word_embeddings"), embed("cls_types", "token_type_embeddings")])
        x = g.norm(n("Concat", [cls, prompt], axis=1), "bert.embeddings.LayerNorm")
        mask = n("Concat", ["attribute_mask", n("Cast", ["attention_mask"], to=TensorProto.FLOAT)], axis=1)
        mask = n("Mul", [n("Sub", ["one", n("Reshape", [mask, "mask_shape"])]), "mask_value"])
        for i in range(layers):
            p = "bert.encoder.layer." + str(i)
            residual = x
            qkv = []
            for key in ("query", "key", "value"):
                qkv.append(n("Transpose", [n("Reshape", [g.linear(x, p + ".attention.self." + key), "heads_shape"])], perm=[0, 2, 1, 3]))
            q, k, v = qkv
            scores = n("Div", [n("MatMul", [q, n("Transpose", [k], perm=[0, 1, 3, 2])]), "attention_scale"])
            probs = n("Softmax", [n("Add", [scores, mask])], axis=-1)
            context = n("Reshape", [n("Transpose", [n("MatMul", [probs, v])], perm=[0, 2, 1, 3]), "merged_shape"])
            x = g.norm(n("Add", [residual, g.linear(context, p + ".attention.output.dense")]), p + ".attention.output.LayerNorm")
            residual = x
            x = g.gelu(g.linear(x, p + ".intermediate.dense"))
            x = g.norm(n("Add", [residual, g.linear(x, p + ".output.dense")]), p + ".output.LayerNorm")
        selected = n("Reshape", [n("Gather", [x, "attribute_indices"], axis=1), "pooled_shape"])
        pooled = n("Tanh", [g.linear(selected, "pooler.dense")])
        logits = []
        for i, definition in enumerate(definitions):
            key = "classifieratt." + str(i)
            require(tuple(state[key + ".weight"].shape) == (len(definition["values"]), hidden), "Classifier/schema mismatch: " + key)
            index = g.constant("head_index_" + str(i), [i], np.int64)
            logits.append(g.linear(n("Gather", [pooled, index], axis=0), key))
        n("Concat", logits, "logits", axis=1)
        g.outputs = [g.info("logits", [1, sum(len(d["values"]) for d in definitions)])]
        g.finish("attributes.onnx", ["bert.pooler.dense.weight", "bert.pooler.dense.bias", "bert.embeddings.position_ids"])
        # Normalize the exact supported tokenizer contract into data consumed by .NET.
        write_json("wordpiece.json", dict(vocabulary=vocabulary, lowercase=True, stripAccents=True,
                                         tokenizeChineseCharacters=True, maxWordCharacters=100,
                                         specialTokens=["[UNK]", "[SEP]", "[PAD]", "[CLS]", "[MASK]"] + ["[unused" + str(i) + "]" for i in range(count)]))
        return dict(kind="text", graph="attributes.onnx", tokenizer="wordpiece.json",
                    maxSequenceLength=config["max_position_embeddings"], classifierCount=count)


require(precision.lower() == "fp32", "MuseCoco staging exports FP32; use ReduceOnnxAsync afterward for INT8/INT4")
os.makedirs(output_path, exist_ok=True)
schema = json.loads(attribute_schema_json)
require(model_kind in ("music", "text"), "Unknown MuseCoco model kind")
metadata = export_music(schema, json.loads(music_vocabulary_json)) if model_kind == "music" else export_text(schema)
metadata.update(format="codebrix.musecoco.v1", schema="music-attributes.json")
write_json("music-attributes.json", schema)
write_json("musecoco.json", metadata)
files = sorted(os.listdir(output_path))
result = dict(tool="codebrix-musecoco", version="1", files=files,
              sizes=[os.path.getsize(os.path.join(output_path, name)) for name in files])
