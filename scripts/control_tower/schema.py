"""Offline, fail-closed evaluator for the checked-in schema's bounded vocabulary.

Not a general JSON Schema library. Unknown keywords/dialects fail closed. No remote refs.
"""
import json
from pathlib import Path
import re

SCHEMA_PATH = Path(__file__).resolve().parents[2] / 'docs/control-tower/handoff.schema.json'
KEYWORDS = {'$schema', '$id', 'title', 'type', 'additionalProperties', 'required', 'properties',
            'const', 'enum', 'pattern', 'minimum', 'maximum', 'minItems', 'maxItems', 'items',
            'allOf', 'if', 'then'}


def strict_loads(text):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise ValueError('DUPLICATE_JSON_KEY')
            result[key] = value
        return result
    def invalid(_):
        raise ValueError('NON_JSON_NUMBER')
    return json.loads(text, object_pairs_hook=pairs, parse_constant=invalid)


def load_schema(path=SCHEMA_PATH):
    schema = strict_loads(path.read_text())
    if schema.get('$schema') != 'https://json-schema.org/draft/2020-12/schema':
        raise ValueError('UNSUPPORTED_SCHEMA')
    def check(node):
        if not isinstance(node, dict) or set(node) - KEYWORDS:
            raise ValueError('UNSUPPORTED_SCHEMA')
        for value in node.get('properties', {}).values():
            check(value)
        for key in ('items', 'if', 'then'):
            if key in node:
                check(node[key])
        for value in node.get('allOf', []):
            check(value)
        if 'type' in node and node['type'] not in ('object', 'array', 'string', 'integer'):
            raise ValueError('UNSUPPORTED_SCHEMA')
    check(schema)
    return schema


def errors(value, schema=None):
    schema = load_schema() if schema is None else schema
    found = []
    def equal(a, b):
        return type(a) is type(b) and a == b
    def walk(value, node, path):
        before = len(found)
        types = {'object': dict, 'array': list, 'string': str, 'integer': int}
        if 'type' in node and type(value) is not types[node['type']]:
            found.append((path, 'TYPE'))
            return
        if 'const' in node and not equal(value, node['const']):
            found.append((path, 'CONST'))
        if 'enum' in node and not any(equal(value, x) for x in node['enum']):
            found.append((path, 'ENUM'))
        if isinstance(value, str) and 'pattern' in node:
            # Full match intentionally rejects a trailing newline after a SHA.
            if re.fullmatch(node['pattern'], value) is None:
                found.append((path, 'PATTERN'))
        if type(value) is int:
            if value < node.get('minimum', value) or value > node.get('maximum', value):
                found.append((path, 'RANGE'))
        if isinstance(value, dict):
            if set(node.get('required', [])) - set(value):
                found.append((path, 'REQUIRED'))
            properties = node.get('properties', {})
            if node.get('additionalProperties') is False and set(value) - set(properties):
                found.append((path, 'EXTRA_FIELDS'))
            for key, child in properties.items():
                if key in value:
                    walk(value[key], child, path + (key,))
        if isinstance(value, list):
            if not node.get('minItems', 0) <= len(value) <= node.get('maxItems', len(value)):
                found.append((path, 'ITEM_COUNT'))
            for item in value:
                if 'items' in node:
                    walk(item, node['items'], path + ('ITEM',))
        for child in node.get('allOf', []):
            walk(value, child, path)
        if 'if' in node:
            trial = len(found)
            walk(value, node['if'], path)
            matches = len(found) == trial
            del found[trial:]
            if matches and 'then' in node:
                walk(value, node['then'], path)
        return len(found) == before
    walk(value, schema, ())
    # Paths only come from the local trusted schema; input keys/values are never exported.
    return sorted(set(found))
