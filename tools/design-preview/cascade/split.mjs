import { readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
const [, , srcDir, bundlePath, out] = process.argv;
const walk = d => readdirSync(d).flatMap(n => statSync(join(d, n)).isDirectory() ? walk(join(d, n)) : [join(d, n)]);
const bundle = readFileSync(bundlePath, 'utf8');
const strip = s => s.replace(/\/\*[\s\S]*?\*\//g, '');
const found = [];
for (const path of walk(srcDir).filter(f => f.endsWith('.css'))) {
  const text = strip(readFileSync(path, 'utf8')).trim();
  // Locate by the first 60 non-space chars of the file's own text (comments removed in both).
  const probe = text.slice(0, 200).replace(/\s+/g, ' ');
  const flat = strip(bundle).replace(/\s+/g, ' ');
  found.push({ file: path.slice(srcDir.length), at: flat.indexOf(probe), len: text.replace(/\s+/g, ' ').length });
}
found.sort((a, b) => a.at - b.at);
const flat = strip(bundle).replace(/\s+/g, ' ');
const segments = found.map((f, i) => ({ file: f.file, css: flat.slice(f.at, i + 1 < found.length ? found[i + 1].at : undefined) }));
console.error(found.map(f => `${f.at}\t${f.file}`).join('\n'));
writeFileSync(out, JSON.stringify(segments));
