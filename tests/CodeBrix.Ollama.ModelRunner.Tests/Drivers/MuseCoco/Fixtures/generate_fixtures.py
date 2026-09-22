"""Manual generator; ordinary tests require only .NET and the generated files.

Usage: python generate_fixtures.py --text-bundle STAGED_FP32_TEXT --bert-source BERT_CHECKPOINT_FOLDER
Requires onnx, numpy, onnxruntime and transformers. Synthetic graph weights and prompts are ours.
The BERT vocabulary comes from the supplied tokenizer; see README.txt for attribution.
"""
import argparse
import json
from pathlib import Path
import shutil

import numpy as np
import onnx
from onnx import helper as h, numpy_helper as nh, TensorProto as T
import onnxruntime as ort
from transformers import AutoTokenizer

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[4]
parser = argparse.ArgumentParser()
parser.add_argument('--text-bundle', required=True)
parser.add_argument('--bert-source', required=True)
args = parser.parse_args()
assets = ROOT / 'src/CodeBrix.Ollama.ModelManager/Export/Assets'
schema = json.loads((assets / 'musecoco-attributes.json').read_text())
vocab = json.loads((assets / 'musecoco-vocabulary.json').read_text())
index = {v: i for i, v in enumerate(vocab)}


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n')


def save(folder, filename, nodes, inputs, outputs, weights):
    graph = h.make_graph(nodes, 'SyntheticMuseCocoDriverFixture', inputs, outputs,
                         [nh.from_array(np.asarray(value), name) for name, value in weights.items()])
    model = h.make_model(graph, opset_imports=[h.make_opsetid('', 17)], ir_version=9)
    onnx.checker.check_model(model)
    onnx.save(model, folder / filename)


music = HERE / 'music'
text = HERE / 'text'
music.mkdir(exist_ok=True)
text.mkdir(exist_ok=True)
for folder in (music, text):
    write_json(folder / 'music-attributes.json', schema)
write_json(music / 'vocabulary.json', vocab)
write_json(music / 'musecoco.json', dict(format='codebrix.musecoco.v1', kind='music', graph='decoder.onnx',
    schema='music-attributes.json', vocabulary='vocabulary.json', layers=1, hiddenSize=2, heads=1,
    headSize=2, positionCount=32, ticksPerQuarterNote=480, positionsPerQuarterNote=12, prefixPositionStart=63))
sequence = ['s-9', 'o-0', 't-32', 'i-0', 'p-60', 'd-12', 'v-20', 'b-1', 'o-0', 'i-128', 'p-164', 'd-6', 'v-24', 'b-1', '</s>']
table = np.full((32, len(vocab)), -1000, dtype=np.float32)
table[:, index['</s>']] = 0
for position, token in enumerate(sequence, 3):
    table[position, :] = -1000
    table[position, index[token]] = 10
# Two plausible pitches at one step let tests exercise the seeded sampler too.
table[7, index['p-64']] = 9.9
save(music, 'decoder.onnx', [h.make_node('Gather', ['table', 'position'], ['logits'], axis=0),
    h.make_node('Add', ['state_s_0', 'one'], ['next_s_0']), h.make_node('Add', ['state_z_0', 'one'], ['next_z_0'])],
    [h.make_tensor_value_info('token', T.INT64, [1]), h.make_tensor_value_info('position', T.INT64, [1]),
     h.make_tensor_value_info('state_s_0', T.FLOAT, [1, 2, 2]), h.make_tensor_value_info('state_z_0', T.FLOAT, [1, 2])],
    [h.make_tensor_value_info('logits', T.FLOAT, [1, len(vocab)]),
     h.make_tensor_value_info('next_s_0', T.FLOAT, [1, 2, 2]), h.make_tensor_value_info('next_z_0', T.FLOAT, [1, 2])],
    dict(table=table, one=np.array(1, dtype=np.float32)))
write_json(music / 'expected.json', dict(tokens=[index[t] for t in sequence[:-1]],
    notes=[dict(program=0, channel=0, pitch=60, tick=0, duration=480, velocity=82),
           dict(program=0, channel=9, pitch=36, tick=1920, duration=240, velocity=98)]))
write_json(text / 'musecoco.json', dict(format='codebrix.musecoco.v1', kind='text', graph='attributes.onnx',
    schema='music-attributes.json', tokenizer='wordpiece.json', maxSequenceLength=512, classifierCount=60))
shutil.copyfile(Path(args.text_bundle) / 'wordpiece.json', text / 'wordpiece.json')
defs = sorted((d for d in schema['definitions'] if d['bertHead'] >= 0), key=lambda d: d['bertHead'])
logits = []
expected = {}
for definition in defs:
    choice = 0 if definition['name'] in ('instrument.piano', 'tempo', 'key') else definition['default']
    expected[definition['name']] = definition['values'][choice]
    values = [-2.] * len(definition['values'])
    values[choice] = 4.
    logits.extend(values)
weights = np.repeat(np.array(logits, dtype=np.float32)[None, :] / 128, 128, axis=0)
save(text, 'attributes.onnx', [h.make_node('MatMul', ['features', 'weights'], ['logits'], name='classifiers.MatMul')],
    [h.make_tensor_value_info(name, T.INT64, [1, 'sequence']) for name in ('input_ids', 'attention_mask', 'token_type_ids', 'position_ids')],
    [h.make_tensor_value_info('logits', T.FLOAT, [1, len(logits)])],
    dict(features=np.ones((1, 128), dtype=np.float32), weights=weights))
write_json(text / 'expected.json', expected)

tokenizer = AutoTokenizer.from_pretrained(args.bert_source, local_files_only=True)
prompts = ['', 'A slow solo piano piece in a major key.', 'Fast energetic rock with guitar and drums.',
    'Café, naïve! [unused0] [MASK]', '[CLS]piano[SEP]', '大提琴与钢琴。', '𠀀 music 丽', 'İSTANBUL résumé',
    'Καλημέρα Σίσυφος', 'hello\u0000\u0001\ufffdworld', 'a\u200bb\t c\n d\r e\u00a0f',
    '😀 music — softly…', 'a' * 101, 'playing pianissimo quickly', 'piano ' * 600]
write_json(HERE / 'tokenizer-oracle.json', [dict(text=prompt,
    tokens=tokenizer(prompt, truncation=True, max_length=512)['input_ids'],
    truncated=len(tokenizer(prompt, truncation=False)['input_ids']) > 512) for prompt in prompts])
# Verify generated graph outputs are valid ONNX Runtime inputs/outputs too.
session = ort.InferenceSession(str(text / 'attributes.onnx'), providers=['CPUExecutionProvider'])
actual = session.run(None, {i.name: np.zeros((1, 2), dtype=np.int64) for i in session.get_inputs()})[0]
np.testing.assert_allclose(actual, np.asarray(logits, dtype=np.float32)[None, :])
