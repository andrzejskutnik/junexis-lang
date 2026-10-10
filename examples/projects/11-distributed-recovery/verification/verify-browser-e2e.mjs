import { spawn } from 'node:child_process';
import { mkdtemp, rm } from 'node:fs/promises';
import { createConnection } from 'node:net';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';

const [serverArgument, reportArgument, browserArgument] = process.argv.slice(2);
if (!serverArgument || !reportArgument || !browserArgument) {
  console.error('usage: node verify-browser-e2e.mjs <server-executable> <report-directory> <browser-executable>');
  process.exit(2);
}

const serverExecutable = resolve(serverArgument);
const reportDirectory = resolve(reportArgument);
const browserExecutable = resolve(browserArgument);
const httpPort = 18200;
const healthPort = 18201;
const reportPort = 18202;
const debugPort = 18203;
const tcpPort = 19201;
const workspace = await mkdtemp(join(tmpdir(), 'junexis-golden-home-browser-'));
const stateDirectory = join(workspace, 'state');
const browserProfile = join(workspace, 'browser');
let server;
let report;
let browser;
let cdp;

function startServer() {
  return spawn(serverExecutable, [
    '--server-mode', 'persistent',
    '--http-port', String(httpPort),
    '--health-port', String(healthPort),
    '--tcp-port', '19200'
  ], { cwd: stateDirectory, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
}

function startReport() {
  return spawn(process.execPath, ['serve.mjs'], {
    cwd: reportDirectory,
    windowsHide: true,
    stdio: ['ignore', 'pipe', 'pipe'],
    env: {
      ...process.env,
      JUNEXIS_REPORT_PORT: String(reportPort),
      JUNEXIS_SERVER_URL: `http://127.0.0.1:${httpPort}`
    }
  });
}

function captureFailure(processHandle, label) {
  let output = '';
  processHandle.stdout?.on('data', chunk => { output += chunk.toString(); });
  processHandle.stderr?.on('data', chunk => { output += chunk.toString(); });
  processHandle.output = () => output;
  processHandle.on('error', error => { output += `\n${label}: ${error.message}`; });
  return processHandle;
}

async function waitForHttp(url, label, processHandle) {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    if (processHandle.exitCode !== null)
      throw new Error(`${label} exited before readiness: ${processHandle.output()}`);
    try {
      const response = await fetch(url);
      if (response.ok) return;
    } catch {}
    await delay(100);
  }
  throw new Error(`${label} readiness timed out: ${processHandle.output()}`);
}

async function stop(processHandle) {
  if (!processHandle || processHandle.exitCode !== null) return;
  const exited = new Promise(resolveExit => processHandle.once('exit', resolveExit));
  processHandle.kill('SIGTERM');
  const graceful = await Promise.race([exited.then(() => true), delay(3_000).then(() => false)]);
  if (!graceful && processHandle.exitCode === null) {
    processHandle.kill('SIGKILL');
    await Promise.race([exited, delay(3_000)]);
  }
}

function delay(milliseconds) {
  return new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));
}

function parseColor(value) {
  const normalized = value.trim().replace(/^#([0-9a-f])([0-9a-f])([0-9a-f])$/i, '#$1$1$2$2$3$3');
  const match = normalized.match(/^#([0-9a-f]{6})$/i);
  if (!match) throw new Error(`unsupported audit color: ${value}`);
  return [0, 2, 4].map(offset => Number.parseInt(match[1].slice(offset, offset + 2), 16));
}

function contrast(foreground, background) {
  const luminance = color => {
    const channels = parseColor(color).map(channel => {
      const normalized = channel / 255;
      return normalized <= 0.04045
        ? normalized / 12.92
        : ((normalized + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
  };
  const first = luminance(foreground);
  const second = luminance(background);
  return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
}

function requireAudit(condition, message) {
  if (!condition) throw new Error(`quality audit failed: ${message}`);
}

async function sendHeartbeat(bootId, sequence) {
  const payload = Buffer.from(JSON.stringify({
    device_id: 'living-room-sensor',
    boot_id: bootId,
    sequence,
    safe_state: true,
    observed_at: new Date().toISOString(),
    _ts_us: Date.now() * 1000
  }));
  const length = Buffer.alloc(4);
  length.writeUInt32BE(payload.length);
  await new Promise((resolveSend, rejectSend) => {
    const socket = createConnection({ host: '127.0.0.1', port: tcpPort }, () => {
      socket.end(Buffer.concat([length, payload, Buffer.from('\n')]));
    });
    socket.once('error', rejectSend);
    socket.once('close', resolveSend);
  });
}

async function connectCdp() {
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline) {
    try {
      const pages = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
      const page = pages.find(candidate => candidate.type === 'page' && candidate.url.includes(String(reportPort)));
      if (page?.webSocketDebuggerUrl) return new Cdp(page.webSocketDebuggerUrl);
    } catch {}
    await delay(100);
  }
  throw new Error('browser debugging endpoint did not expose the report page');
}

class Cdp {
  constructor(url) {
    this.socket = new WebSocket(url);
    this.nextId = 1;
    this.pending = new Map();
    this.ready = new Promise((resolveReady, rejectReady) => {
      this.socket.addEventListener('open', resolveReady, { once: true });
      this.socket.addEventListener('error', rejectReady, { once: true });
    });
    this.socket.addEventListener('message', event => {
      const message = JSON.parse(event.data);
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message));
      else pending.resolve(message.result);
    });
  }

  async call(method, params = {}) {
    await this.ready;
    const id = this.nextId++;
    const result = new Promise((resolveCall, rejectCall) => {
      this.pending.set(id, { resolve: resolveCall, reject: rejectCall });
    });
    this.socket.send(JSON.stringify({ id, method, params }));
    return result;
  }

  async evaluate(expression) {
    const result = await this.call('Runtime.evaluate', {
      expression,
      returnByValue: true,
      awaitPromise: true
    });
    if (result.exceptionDetails)
      throw new Error(result.exceptionDetails.text ?? 'browser evaluation failed');
    return result.result.value;
  }

  close() {
    this.socket.close();
  }
}

async function waitForBrowser(predicate, label) {
  const deadline = Date.now() + 20_000;
  let lastSnapshot;
  while (Date.now() < deadline) {
    const snapshot = await cdp.evaluate(`(() => ({
      url: location.href,
      documentTitle: document.title,
      readyState: document.readyState,
      body: document.body?.innerText?.slice(0, 500) ?? '',
      title: document.querySelector('#report-title')?.textContent ?? '',
      connection: document.querySelector('#connection-status')?.dataset.state ?? '',
      connectionText: document.querySelector('#connection-label')?.textContent ?? '',
      panel: document.querySelector('#report-panels')?.innerText ?? '',
      stale: document.querySelector('.report-panel')?.getAttribute('data-stale') ?? ''
    }))()`);
    lastSnapshot = snapshot;
    if (predicate(snapshot)) return snapshot;
    await delay(100);
  }
  throw new Error(`browser did not reach phase: ${label}; last DOM state: ${JSON.stringify(lastSnapshot)}`);
}

async function auditTheme(scheme) {
  await cdp.call('Emulation.setEmulatedMedia', {
    media: 'screen',
    features: [{ name: 'prefers-color-scheme', value: scheme }]
  });
  const colors = await cdp.evaluate(`(() => {
    const style = getComputedStyle(document.documentElement);
    return Object.fromEntries(['bg', 'surface', 'text', 'muted', 'accent', 'warn']
      .map(name => [name, style.getPropertyValue('--' + name).trim()]));
  })()`);
  const ratios = {
    textOnBackground: contrast(colors.text, colors.bg),
    mutedOnBackground: contrast(colors.muted, colors.bg),
    textOnSurface: contrast(colors.text, colors.surface),
    accentOnBackground: contrast(colors.accent, colors.bg),
    warningOnSurface: contrast(colors.warn, colors.surface)
  };
  Object.entries(ratios).forEach(([name, ratio]) =>
    requireAudit(ratio >= 4.5, `${scheme} ${name} contrast ${ratio.toFixed(2)} is below WCAG AA`));
  return { colors, ratios };
}

async function auditQuality() {
  const semantics = await cdp.evaluate(`(() => ({
    language: document.documentElement.lang,
    h1Count: document.querySelectorAll('h1').length,
    statusRole: document.querySelector('#connection-status')?.getAttribute('role') ?? '',
    statusLive: document.querySelector('#connection-status')?.getAttribute('aria-live') ?? '',
    mainTabIndex: document.querySelector('main')?.getAttribute('tabindex') ?? '',
    emptyState: document.querySelector('.empty-state')?.textContent ?? ''
  }))()`);
  requireAudit(semantics.language === 'pl', 'document language must be pl');
  requireAudit(semantics.h1Count === 1, 'document must contain exactly one h1');
  requireAudit(semantics.statusRole === 'status' && semantics.statusLive === 'polite', 'connection status must be a polite live region');
  requireAudit(semantics.mainTabIndex === '-1', 'skip-link target must be programmatically focusable');
  requireAudit(semantics.emptyState.includes('Oczekiwanie'), 'connected empty state must be visible');

  const dark = await auditTheme('dark');
  const light = await auditTheme('light');

  await cdp.call('Emulation.setDeviceMetricsOverride', {
    width: 390,
    height: 844,
    deviceScaleFactor: 1,
    mobile: true
  });
  const mobile = await cdp.evaluate(`(() => ({
    viewportWidth: innerWidth,
    documentWidth: document.documentElement.scrollWidth,
    headerDirection: getComputedStyle(document.querySelector('.report-header')).flexDirection,
    panelWidth: Math.round(document.querySelector('.report-panel').getBoundingClientRect().width)
  }))()`);
  requireAudit(mobile.documentWidth <= mobile.viewportWidth, '390px viewport must not scroll horizontally');
  requireAudit(mobile.headerDirection === 'column', 'mobile header must stack vertically');
  requireAudit(mobile.panelWidth <= mobile.viewportWidth - 16, 'mobile panel must stay inside viewport');

  await cdp.evaluate(`document.activeElement?.blur()`);
  await cdp.call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
  await cdp.call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
  const keyboardFocus = await cdp.evaluate(`(() => ({
    className: document.activeElement?.className ?? '',
    label: document.activeElement?.textContent ?? '',
    outlineWidth: getComputedStyle(document.activeElement).outlineWidth,
    top: getComputedStyle(document.activeElement).top
  }))()`);
  requireAudit(String(keyboardFocus.className).includes('skip-link'), 'first Tab must focus the skip link');
  requireAudit(keyboardFocus.outlineWidth === '3px', 'keyboard focus must have a 3px outline');
  requireAudit(keyboardFocus.top === '16px', 'focused skip link must be visible');
  await cdp.call('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
  await cdp.call('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
  await delay(100);
  const skipTarget = await cdp.evaluate(`document.activeElement?.id ?? ''`);
  requireAudit(skipTarget === 'report-content', 'skip link must focus report-content');

  await cdp.call('Emulation.setEmulatedMedia', {
    media: 'screen',
    features: [
      { name: 'prefers-color-scheme', value: 'dark' },
      { name: 'prefers-reduced-motion', value: 'reduce' }
    ]
  });
  const reducedMotion = await cdp.evaluate(`(() => {
    const style = getComputedStyle(document.querySelector('.report-panel'));
    return { transitionDuration: style.transitionDuration, animationDuration: style.animationDuration };
  })()`);
  requireAudit(reducedMotion.transitionDuration === '0s', 'reduced motion must disable transitions');
  requireAudit(reducedMotion.animationDuration === '0s', 'reduced motion must disable animations');

  await cdp.call('Emulation.clearDeviceMetricsOverride');
  await cdp.call('Emulation.setEmulatedMedia', { media: 'screen', features: [] });
  return { semantics, themes: { dark, light }, mobile, keyboardFocus, reducedMotion };
}

async function auditBoundedRenderer() {
  const probe = await cdp.evaluate(`(async () => {
    const module = await import('./dist/report-view.js');
    const payload = { items: Array.from({ length: 100 }, (_, index) => index), note: 'x'.repeat(5000) };
    for (let index = 0; index < 55; index += 1) module.renderReportItem('QualityProbe', payload);
    const panels = [...document.querySelectorAll('.report-panel')];
    const panel = panels.find(candidate => candidate.id.includes('QualityProbe'));
    return { text: panel?.innerText ?? '', length: panel?.innerText.length ?? 0 };
  })()`);
  requireAudit(probe.text.includes('Ukryto 80 dalszych elementów.'), 'collection renderer must cap visible items at 20');
  requireAudit(probe.text.includes('Historia w pamięci: 50 / 50'), 'history must stay capped at 50');
  requireAudit(probe.text.includes('…'), 'long text must be visibly truncated');
  requireAudit(probe.length < 6_000, 'bounded payload DOM must stay below 6000 visible characters');
  return { visibleTextLength: probe.length, collectionRemainder: 80, history: '50 / 50', longTextTruncated: true };
}

try {
  await import('node:fs/promises').then(fs => fs.mkdir(stateDirectory, { recursive: true }));
  server = captureFailure(startServer(), 'server');
  await waitForHttp(`http://127.0.0.1:${healthPort}/health/ready`, 'server', server);
  report = captureFailure(startReport(), 'report');
  await waitForHttp(`http://127.0.0.1:${reportPort}/health/ready`, 'report', report);

  const panelUrl = `http://127.0.0.1:${reportPort}/?server=${encodeURIComponent(`http://127.0.0.1:${httpPort}`)}`;
  browser = captureFailure(spawn(browserExecutable, [
    '--headless=new',
    '--disable-gpu',
    '--no-first-run',
    '--no-default-browser-check',
    `--remote-debugging-port=${debugPort}`,
    `--user-data-dir=${browserProfile}`,
    panelUrl
  ], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] }), 'browser');
  cdp = await connectCdp();

  const connected = await waitForBrowser(
    snapshot => snapshot.connection === 'connected' && snapshot.title.includes('Dom 11'),
    'connected');
  const quality = await auditQuality();

  await sendHeartbeat('boot-alpha', 1);
  const online = await waitForBrowser(
    snapshot => snapshot.panel.includes('online')
      && snapshot.panel.includes('first-heartbeat')
      && snapshot.panel.includes('living-room-sensor'),
    'first online observation');

  await stop(server);
  server = undefined;
  const reconnecting = await waitForBrowser(
    snapshot => snapshot.connection === 'reconnecting'
      && snapshot.stale === 'true'
      && snapshot.connectionText.includes('Połączenie przerwane'),
    'visible reconnect');

  server = captureFailure(startServer(), 'server restart');
  await waitForHttp(`http://127.0.0.1:${healthPort}/health/ready`, 'server restart', server);
  await waitForBrowser(snapshot => snapshot.connection === 'connected', 'reconnected');
  await sendHeartbeat('boot-beta', 1);
  const rejoined = await waitForBrowser(
    snapshot => snapshot.panel.includes('rejoined')
      && snapshot.panel.includes('device-boot-identity-changed')
      && snapshot.panel.includes('Generacja połączenia\n1'),
    'rejoined observation');
  const boundedRenderer = await auditBoundedRenderer();

  console.log(JSON.stringify({
    result: 'PASS',
    engine: 'Chromium',
    phases: {
      connected,
      online,
      reconnecting,
      rejoined
    },
    quality,
    boundedRenderer
  }, null, 2));
} finally {
  cdp?.close();
  await stop(browser);
  await stop(report);
  await stop(server);
  await rm(workspace, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
