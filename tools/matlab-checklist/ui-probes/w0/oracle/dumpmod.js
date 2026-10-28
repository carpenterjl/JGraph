// Prints one module's raw text from the bundle: node dumpmod.js <module> [maxChars]
const { moduleText } = require('./amd');
const t = moduleText(process.argv[2]);
if (t === null) { console.error('no module ' + process.argv[2]); process.exit(1); }
const max = +(process.argv[3] || 1e9);
console.log(t.length > max ? t.slice(0, max) + ' ...[' + t.length + ']' : t);
