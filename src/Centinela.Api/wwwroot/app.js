'use strict';

// Todo el contenido que llega de la API (texto de normas, borradores, justificaciones) se escribe con textContent y
// nunca como HTML: lo han generado modelos a partir de texto externo y no se puede confiar en él.

const STATUS = {
  AwaitingHumanApproval: 'Pendiente de aprobación', Approved: 'Aprobado', Rejected: 'Rechazado',
  Failed: 'Fallido', Discarded: 'Descartado', UnderAudit: 'En auditoría', Detected: 'Detectado',
};
const SEVERITY = { None: 'Sin impacto', Low: 'Baja', Medium: 'Media', High: 'Alta', Critical: 'Crítica' };

const $ = (id) => document.getElementById(id);
let selected = null;

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (k === 'class') node.className = v;
    else if (k === 'text') node.textContent = v;
    else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
    else node.setAttribute(k, v);
  }
  for (const c of children.flat()) if (c != null) node.append(c);
  return node;
}

async function api(path, options = {}) {
  const key = sessionStorage.getItem('centinela-key') || '';
  const res = await fetch('/api' + path, {
    ...options,
    headers: { 'X-Api-Key': key, ...(options.body ? { 'Content-Type': 'application/json' } : {}) },
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw Object.assign(new Error(data.error || res.statusText), { status: res.status, data });
  return data;
}

// ───── lista ─────

async function loadList() {
  const msg = $('list-msg');
  msg.textContent = 'Cargando…';
  try {
    const status = $('filter').value;
    const cases = await api('/cases' + (status ? `?status=${status}` : ''));
    const ul = $('cases');
    ul.replaceChildren();
    msg.textContent = cases.length ? '' : 'No hay casos con ese estado.';
    for (const c of cases) {
      const li = el('li', { onclick: () => openCase(c.id), 'data-id': c.id },
        el('div', { class: 't', text: c.title }),
        el('div', { class: 'meta', text: `${c.sourceId} · ${c.publishedOn}` }),
        el('div', {},
          el('span', { class: 'badge', text: STATUS[c.status] || c.status }),
          c.escalated ? el('span', { class: 'badge bad', text: 'Auditoría no superada' }) : null,
          c.pendingData ? el('span', { class: 'badge warn', text: `${c.pendingData} por completar` }) : null,
          el('span', { class: 'meta', text: `${c.actions} borrador(es)` })));
      if (c.id === selected) li.classList.add('sel');
      ul.append(li);
    }
  } catch (e) {
    msg.textContent = e.status === 401 ? 'Clave incorrecta o ausente.' : `Error: ${e.message}`;
    $('cases').replaceChildren();
  }
}

// ───── diferencias palabra a palabra (LCS) ─────

function diff(a, b) {
  const x = a.split(/(\s+)/), y = b.split(/(\s+)/);
  const n = x.length, m = y.length;
  const t = Array.from({ length: n + 1 }, () => new Uint16Array(m + 1));
  for (let i = n - 1; i >= 0; i--)
    for (let j = m - 1; j >= 0; j--)
      t[i][j] = x[i] === y[j] ? t[i + 1][j + 1] + 1 : Math.max(t[i + 1][j], t[i][j + 1]);
  const out = [];
  let i = 0, j = 0;
  while (i < n && j < m) {
    if (x[i] === y[j]) { out.push(['=', x[i]]); i++; j++; }
    else if (t[i + 1][j] >= t[i][j + 1]) out.push(['-', x[i++]]);
    else out.push(['+', y[j++]]);
  }
  while (i < n) out.push(['-', x[i++]]);
  while (j < m) out.push(['+', y[j++]]);
  return out;
}

function highlightTodo(text, wrapper) {
  const re = /\[COMPLETAR:[^\]]*\]/g;
  let last = 0, m;
  while ((m = re.exec(text))) {
    wrapper.append(text.slice(last, m.index), el('mark', { text: m[0] }));
    last = m.index + m[0].length;
  }
  wrapper.append(text.slice(last));
}

function diffNode(original, proposed, side) {
  const box = el('div', { class: 'text' });
  if (side === 'proposed') {
    for (const [op, tok] of diff(original, proposed)) {
      if (op === '-') continue;
      if (op === '+' && tok.trim()) { const ins = el('ins'); highlightTodo(tok, ins); box.append(ins); }
      else highlightTodo(tok, box);
    }
  } else {
    for (const [op, tok] of diff(original, proposed)) {
      if (op === '+') continue;
      box.append(op === '-' && tok.trim() ? el('del', { text: tok }) : tok);
    }
  }
  return box;
}

// ───── detalle ─────

async function openCase(id) {
  selected = id;
  for (const li of document.querySelectorAll('.cases li')) li.classList.toggle('sel', li.dataset.id === id);
  const pane = $('detail-pane');
  pane.replaceChildren(el('p', { class: 'msg', text: 'Cargando…' }));
  try {
    render(await api(`/cases/${id}`));
  } catch (e) {
    pane.replaceChildren(el('p', { class: 'msg', text: `Error: ${e.message}` }));
  }
}

function render(c) {
  const pane = $('detail-pane');
  const parts = [];

  parts.push(
    el('h2', { text: c.title }),
    el('div', { class: 'meta' },
      `${c.sourceId} · ${c.publishedOn} · `, el('a', { href: c.url, target: '_blank', rel: 'noopener noreferrer', text: 'ver en el BOE' }),
      ` · ${c.revision} ronda(s) de redacción · `, el('span', { class: 'badge', text: STATUS[c.status] || c.status })));

  if (c.risksToAcknowledge.length) {
    parts.push(el('div', { class: 'banner', role: 'alert' },
      el('strong', { text: 'Antes de aprobar, ten en cuenta:' }),
      el('ul', {}, c.risksToAcknowledge.map((r) => el('li', { text: r })))));
  }

  if (c.analysisSummary) {
    // El resumen es la suma de los resúmenes de cada fragmento: se enseñan unos pocos y el resto va plegado.
    const lines = c.analysisSummary.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
    parts.push(el('h3', { text: 'Qué cambia' }),
      ...lines.slice(0, 3).map((l) => el('p', { text: l })),
      lines.length > 3
        ? el('details', {}, el('summary', { text: `Ver los otros ${lines.length - 3} fragmentos` }), lines.slice(3).map((l) => el('p', { text: l })))
        : null,
      el('p', { class: 'meta', text: `${c.obligations} obligaciones analizadas · ${c.unsupportedClaims} no respaldadas por su cita.` }));
  }

  if (c.findings.length) {
    parts.push(el('h3', { text: `Documentos afectados (${c.findings.length})` }));
    for (const f of c.findings) {
      parts.push(el('div', { class: 'action' },
        el('strong', { text: `${f.document}${f.passage ? ' · ' + f.passage : ''}` }), ' ',
        el('span', { class: 'badge', text: SEVERITY[f.severity] || f.severity }),
        f.effect ? el('span', { class: 'badge', text: f.effect }) : null,
        el('p', { text: f.rationale }),
        f.quote ? el('p', { class: 'meta', text: `Frase que muestra el problema: «${f.quote}»` }) : null));
    }
  }

  if (c.actions.length) {
    parts.push(el('h3', { text: `Cambios propuestos (${c.actions.length})` }));
    for (const a of c.actions) {
      parts.push(el('div', { class: 'action' },
        el('strong', { text: `${a.documentId}${a.passage ? ' · ' + a.passage : ''}` }),
        el('div', { class: 'sides' },
          el('div', { class: 'side' }, el('h4', { text: 'Original (lo borrado, en rojo)' }), diffNode(a.original || '', a.proposed, 'original')),
          el('div', { class: 'side' }, el('h4', { text: 'Propuesto (lo añadido, en verde)' }), diffNode(a.original || '', a.proposed, 'proposed'))),
        el('p', { text: a.justification }),
        a.pendingData.length
          ? el('p', {}, el('mark', { text: 'Datos que debe aportar la empresa: ' }), ' ' + a.pendingData.join(' · ')) : null,
        a.coverage.length
          ? el('details', {}, el('summary', { text: 'Dónde cumple cada obligación' }),
              el('ul', {}, a.coverage.map((x) => el('li', {}, el('em', { text: x.obligation }), ` → «${x.quote}»`)))) : null,
        a.citations.length
          ? el('details', {}, el('summary', { text: `Norma citada (${a.citations.length})` }),
              a.citations.map((x) => el('div', {},
                el('strong', { text: x.label || x.id }),
                el('div', { class: 'text', text: x.text || '(texto no conservado)' })))) : null));
    }
  }

  if (c.auditIssues.length) {
    parts.push(el('h3', { text: c.auditPassed ? 'Observaciones del auditor' : 'Incidencias sin resolver' }),
      el('ul', {}, c.auditIssues.map((i) => el('li', { text: i }))));
  }

  if (c.usage && c.usage.length) {
    const fmt = (n) => n.toLocaleString('es-ES');
    parts.push(
      el('h3', { text: 'Consumo de modelos' }),
      el('table', { class: 'usage' },
        el('thead', {}, el('tr', {}, ['Etapa', 'Modelo', 'Llamadas', 'Entrada', 'Salida', 'USD est.'].map((h) => el('th', { text: h })))),
        el('tbody', {}, c.usage.map((u) => el('tr', {},
          el('td', { text: u.stage }), el('td', { text: u.model }), el('td', { text: fmt(u.calls) }),
          el('td', { text: fmt(u.inputTokens) }), el('td', { text: fmt(u.outputTokens) }),
          el('td', { text: u.estimatedUsd.toFixed(3) }))))),
      el('p', {
        class: 'meta',
        text: `Total estimado: ${c.estimatedUsd.toFixed(2)} USD con precios de referencia; no es tu factura.` +
          (c.unpricedModels.length ? ` Sin precio: ${c.unpricedModels.join(', ')}.` : ''),
      }));
  }

  parts.push(decisionForm(c));
  parts.push(el('h3', { text: 'Historial' }), el('div', { class: 'log', text: c.log.join('\n') }));
  pane.replaceChildren(...parts);
}

function decisionForm(c) {
  if (!c.canDecide) {
    return el('p', { class: 'msg', text: c.reviewerComment ? `Rechazado: ${c.reviewerComment}` : 'Este caso ya no admite decisión.' });
  }

  const reviewer = el('input', { type: 'text', placeholder: 'Tu nombre', value: localStorage.getItem('centinela-reviewer') || '' });
  const ack = el('input', { type: 'checkbox', id: 'ack' });
  const comment = el('textarea', { placeholder: 'Motivo (obligatorio para rechazar)' });
  const out = el('p', { class: 'msg', role: 'status' });

  async function send(action, body, question) {
    if (!reviewer.value.trim()) { out.textContent = 'Escribe tu nombre.'; return; }
    if (!confirm(question)) return;
    localStorage.setItem('centinela-reviewer', reviewer.value.trim());
    try {
      render(await api(`/cases/${c.id}/${action}`, { method: 'POST', body: JSON.stringify(body) }));
      loadList();
    } catch (e) {
      out.textContent = e.data?.risks ? `${e.message}` : `No se pudo: ${e.message}`;
    }
  }

  const approve = el('button', {
    class: 'primary', type: 'button',
    onclick: () => send('approve', { reviewer: reviewer.value.trim(), acknowledgeRisks: ack.checked },
      'Aprobar deja constancia de que revisaste los cambios. Centinela NO modifica ningún documento: aplicarlos sigue siendo cosa tuya. ¿Aprobar?'),
    text: 'Aprobar',
  });
  const reject = el('button', {
    class: 'danger', type: 'button',
    onclick: () => send('reject', { reviewer: reviewer.value.trim(), comment: comment.value.trim() }, '¿Rechazar este caso?'),
    text: 'Rechazar',
  });

  return el('div', { class: 'decision' },
    el('h3', { text: 'Decisión' }),
    el('div', { class: 'row' }, el('label', { for: 'rev', text: 'Revisor' }), reviewer),
    c.risksToAcknowledge.length
      ? el('label', { class: 'row' }, ack, 'He leído los avisos de arriba y los asumo.') : null,
    comment,
    el('div', { class: 'row' }, approve, reject),
    out);
}

// ───── arranque ─────

$('key-form').addEventListener('submit', (e) => {
  e.preventDefault();
  sessionStorage.setItem('centinela-key', $('key').value);
  $('key').value = '';
  loadList();
});
$('filter').addEventListener('change', loadList);
$('reload').addEventListener('click', loadList);
// En modo demostración el servidor publica /demo.json y el panel entra solo; fuera de la demo ese endpoint no existe.
async function start() {
  try {
    const res = await fetch('/demo.json');
    if (res.ok) {
      const demo = await res.json();
      if (demo.demo) {
        sessionStorage.setItem('centinela-key', demo.apiKey);
        $('demo-banner').hidden = false;
        $('key-form').hidden = true;
      }
    }
  } catch { /* sin demo: se pide la clave */ }

  if (sessionStorage.getItem('centinela-key')) {
    await loadList();
    // ?caso=<id> abre un caso directamente (útil para enlazarlo o para capturas).
    const params = new URLSearchParams(location.search);
    const id = params.get('caso');
    if (id) {
      await openCase(id);
      // ?ir=cambios o ?ir=consumo llevan la vista a esa sección (útil para enlazar y para capturas).
      const target = { cambios: '.sides', consumo: '.usage' }[params.get('ir')];
      if (target) document.querySelector(target)?.scrollIntoView();
    }
  } else {
    $('list-msg').textContent = 'Introduce la clave de la API para ver los casos.';
  }
}

start();
