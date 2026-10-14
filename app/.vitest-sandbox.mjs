// Temporary sandbox runner: vitest without esbuild/child spawns (sandbox blocks piped stdio spawns).
// - patches child_process.exec BEFORE vitest is imported (vite's `exec("net use")` realpath probe -> EPERM)
// - config:false  -> skip vite.config.ts (bundling it would need esbuild)
// - esbuild:false -> disable vite:esbuild transform plugin
// - typescript.transpileModule (pure JS) transpiles .ts files instead
// - pool:'threads' -> worker_threads instead of child_process forks
import { createRequire } from 'node:module';

const require_ = createRequire(import.meta.url);
const cp = require_('node:child_process');
cp.exec = function sandboxExec(cmd, opts, cb) {
  const callback = typeof opts === 'function' ? opts : cb;
  if (callback) process.nextTick(callback, new Error('sandbox: exec disabled'), '', '');
  return { on() {}, once() {}, unref() {}, kill() {}, stdout: null, stderr: null };
};

const ts = (await import('typescript')).default;
const { startVitest } = await import('vitest/node');

const TS_RE = /\.[cm]?tsx?$/;

const tsTranspilePlugin = {
  name: 'sandbox-ts-transpile',
  enforce: 'pre',
  transform(code, id) {
    const file = id.split('?')[0].split('#')[0];
    if (!TS_RE.test(file) || file.endsWith('.d.ts')) return null;
    const out = ts.transpileModule(code, {
      fileName: file,
      reportDiagnostics: false,
      compilerOptions: {
        module: ts.ModuleKind.ESNext,
        target: ts.ScriptTarget.ES2022,
        useDefineForClassFields: false,
        esModuleInterop: true,
        isolatedModules: true,
        inlineSourceMap: true,
        inlineSources: true,
      },
    });
    const map = out.sourceMapText ? JSON.parse(out.sourceMapText) : null;
    return { code: out.outputText, map };
  },
};

const ctx = await startVitest(
  'test',
  process.argv.slice(2),
  {
    root: process.cwd(),
    config: false,
    watch: false,
    run: true,
    pool: 'threads',
  },
  {
    esbuild: false,
    plugins: [tsTranspilePlugin],
    optimizeDeps: { noDiscovery: true, include: [] },
  },
);

let failed = 0;
if (ctx) {
  failed = ctx.state.getFiles().filter((f) => f.result?.state === 'fail').length;
  if (ctx.state.getUnhandledErrors().length) failed++;
}
console.log(failed === 0 ? '\nSANDBOX-RUNNER: ALL FILES PASSED' : `\nSANDBOX-RUNNER: ${failed} FILE(S) FAILED`);
process.exit(failed === 0 ? 0 : 1);
