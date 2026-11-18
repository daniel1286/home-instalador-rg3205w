// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

// La placa del RG3205W vista por detrás, muy simplificada: solo lo que sirve para saber la revisión. Arriba a la
// derecha, el USB-C (Rev4) o su hueco vacío con el J4902 al lado (Rev5); abajo a la derecha, la serigrafía.
//   rev: 4 | 5     cerca: true dibuja solo la esquina, más grande, con los pines del J4902
const PCB = '#24372c', PCB2 = '#2f4638', PISTA = '#3c5a47', SERIG = '#d9e3d4', A = '#e0a85a';

function usbc(x, y, soldado) {
  if (soldado) return `<rect x="${x}" y="${y}" width="30" height="12" rx="6" fill="#b9b4ab" stroke="#ddd6ca" stroke-width="1"/>
    <rect x="${x + 6}" y="${y + 4}" width="18" height="4" rx="2" fill="#4a4741"/>`;
  // Hueco: las patas sin conector
  let p = `<rect x="${x}" y="${y}" width="30" height="12" rx="6" fill="none" stroke="${SERIG}" stroke-width="0.9" stroke-dasharray="2.5 2"/>`;
  for (let i = 0; i < 8; i++) p += `<rect x="${x + 4 + i * 3}" y="${y + 13.5}" width="1.6" height="3" fill="#c9a86a"/>`;
  return p;
}

function j4902(x, y, escala = 1, nombres = false) {
  const w = 34 * escala, h = 11 * escala;
  let s = `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${1.5 * escala}" fill="#f1ede4" stroke="#bdb6a8" stroke-width="${0.8 * escala}"/>`;
  const etiquetas = ['D−', 'D+', 'GND', '+5V'];
  for (let i = 0; i < 4; i++) {
    const cx = x + (5 + i * 8) * escala;
    s += `<rect x="${cx - 2 * escala}" y="${y + 3 * escala}" width="${4 * escala}" height="${5 * escala}" rx="${0.6 * escala}" fill="#c9a86a" stroke="#8a7346" stroke-width="${0.4 * escala}"/>`;
    const letra = Math.min(4.2 * escala, 12);
    if (nombres) s += `<text x="${cx}" y="${y + h + letra + 4}" font-size="${letra}" font-weight="700" fill="${i === 3 ? A : '#f2efe9'}" text-anchor="middle">${etiquetas[i]}</text>`;
  }
  return s;
}

function placa({ rev = 5, ancho = 230 } = {}) {
  const zona = rev === 4
    ? usbc(152, 22, true)
    : usbc(152, 22, false) + j4902(150, 46) + `<text x="167" y="67" font-size="7" font-weight="700" fill="${A}" text-anchor="middle">J4902</text>`;
  return `<svg width="${ancho}" height="${ancho * 150 / 210}" viewBox="0 0 210 150" xmlns="http://www.w3.org/2000/svg">
    <rect x="8" y="10" width="194" height="128" rx="6" fill="${PCB}" stroke="${PCB2}" stroke-width="2"/>
    <path d="M30 40 H90 V70 H120 M30 100 H70 V120 M100 30 V55 H140 M60 60 V90 H110 V110 H150" fill="none" stroke="${PISTA}" stroke-width="1.6"/>
    <rect x="70" y="58" width="44" height="34" rx="2" fill="#1a1a1a" stroke="#3a3a3a"/>
    <rect x="28" y="70" width="22" height="16" rx="1.5" fill="#1a1a1a" stroke="#3a3a3a"/>
    <circle cx="18" cy="20" r="3.5" fill="#1b1a18" stroke="#c9a86a"/><circle cx="192" cy="128" r="3.5" fill="#1b1a18" stroke="#c9a86a"/>
    <circle cx="18" cy="128" r="3.5" fill="#1b1a18" stroke="#c9a86a"/><circle cx="192" cy="20" r="3.5" fill="#1b1a18" stroke="#c9a86a"/>
    <rect x="138" y="14" width="60" height="62" rx="6" fill="none" stroke="${A}" stroke-width="1.6" stroke-dasharray="4 3"/>
    ${zona}
    <text x="176" y="126" font-size="8" font-weight="700" fill="${SERIG}" text-anchor="end">Rev${rev}</text>
    <rect x="142" y="117" width="38" height="13" rx="3" fill="none" stroke="${A}" stroke-width="1.4"/>
  </svg>`;
}

// La esquina del Rev5 de cerca, con el orden de los pines del J4902
function esquinaRev5({ ancho = 480 } = {}) {
  return `<svg width="${ancho}" height="${ancho * 160 / 320}" viewBox="0 0 320 160" xmlns="http://www.w3.org/2000/svg">
    <rect x="6" y="6" width="308" height="148" rx="10" fill="${PCB}" stroke="${PCB2}" stroke-width="2"/>
    <path d="M20 140 H66 V104 M40 30 H100" fill="none" stroke="${PISTA}" stroke-width="2"/>
    <g transform="translate(118 18) scale(2.4)">${usbc(0, 0, false)}</g>
    <text x="154" y="76" font-size="11" fill="${SERIG}" text-anchor="middle">hueco del USB-C (vacío)</text>
    ${j4902(86, 86, 4, true)}
    <text x="236" y="114" font-size="14" font-weight="700" fill="${A}">J4902</text>
  </svg>`;
}
