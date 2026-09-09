#!/usr/bin/env node

import fs from 'node:fs';

function stripCommentsAndStrings(sql) {
  let output = '';
  let state = 'code';
  for (let i = 0; i < sql.length; i += 1) {
    const current = sql[i];
    const next = sql[i + 1];
    if (state === 'code' && current === "'") state = 'string';
    else if (state === 'code' && current === '-' && next === '-') { state = 'line-comment'; i += 1; output += '  '; continue; }
    else if (state === 'code' && current === '/' && next === '*') { state = 'block-comment'; i += 1; output += '  '; continue; }
    else if (state === 'string' && current === "'" && next === "'") { i += 1; output += '  '; continue; }
    else if (state === 'string' && current === "'") state = 'code';
    else if (state === 'line-comment' && current === '\n') state = 'code';
    else if (state === 'block-comment' && current === '*' && next === '/') { state = 'code'; i += 1; output += '  '; continue; }

    output += state === 'code' || current === '\n' ? current : ' ';
  }
  return output;
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

let failures = 0;
for (const file of process.argv.slice(2)) {
  const sql = stripCommentsAndStrings(fs.readFileSync(file, 'utf8'));
  const addPattern = /ALTER\s+TABLE\s+(?:\[?dbo\]?\.)?\[?([A-Za-z0-9_]+)\]?\s+ADD\s+([\s\S]*?);/gi;
  for (const match of sql.matchAll(addPattern)) {
    const table = match[1];
    const body = match[2];
    if (/^\s*CONSTRAINT\b/i.test(body)) continue;

    const columns = body.split('\n')
      .map(line => line.replace(/^\s*,?\s*/, ''))
      .map(line => line.match(/^\[?([A-Za-z_][A-Za-z0-9_]*)\]?\s+/)?.[1])
      .filter(column => column && !['CONSTRAINT', 'FOREIGN', 'CHECK'].includes(column.toUpperCase()));
    const remainder = sql.slice(match.index + match[0].length);
    const tablePattern = escapeRegex(table);
    const laterStatements = [
      ...remainder.matchAll(new RegExp(`ALTER\\s+TABLE\\s+(?:\\[?dbo\\]?\\.)?\\[?${tablePattern}\\]?[\\s\\S]*?;`, 'gi')),
      ...remainder.matchAll(new RegExp(`CREATE\\s+(?:UNIQUE\\s+)?INDEX[\\s\\S]*?ON\\s+(?:\\[?dbo\\]?\\.)?\\[?${tablePattern}\\]?[\\s\\S]*?;`, 'gi')),
      ...remainder.matchAll(new RegExp(`UPDATE[\\s\\S]*?FROM\\s+(?:\\[?dbo\\]?\\.)?\\[?${tablePattern}\\]?[\\s\\S]*?;`, 'gi'))
    ].map(item => item[0]);

    for (const column of columns) {
      const columnPattern = new RegExp(`\\b${escapeRegex(column)}\\b`, 'i');
      if (laterStatements.some(statement => columnPattern.test(statement))) {
        console.error(`${file}: unsafe static same-batch reference after ADD ${table}.${column}`);
        failures += 1;
      }
    }
  }
}

if (failures > 0) process.exit(1);
console.log('SQL ADD-column ordering scan passed.');
