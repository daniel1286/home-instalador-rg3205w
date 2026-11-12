// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

// La Askey dibujada de frente (la misma silueta que el icono de Home), con lo que se ve en su pantalla y qué botones se
// pulsan. Arriba, los dos de volumen; el de encendido está detrás y sale en un globo a la derecha.
//   pantalla: 'apagada' | 'azul' | 'negra' | 'menu'
//   vol, enc: 'suelto' | 'pulsado' | 'una' (pulsar una vez)
function aparato({ pantalla = 'apagada', vol = 'suelto', enc = 'suelto', ancho = 190 } = {}) {
  const A = '#e0a85a', GRIS = '#55504a', GRIS2 = '#77716a';
  // Con «una vez» los dos de volumen se quedan sin marcar: cuál es el «+» lo dice el texto
  const boton = e => e === 'pulsado' ? `fill="${A}" stroke="${A}"` : `fill="${GRIS}" stroke="${GRIS2}"`;
  const brillo = e => e === 'pulsado' ? `filter="url(#luz)"` : '';
  let dentro = '';
  if (pantalla === 'apagada') dentro = `<rect x="36" y="46" width="80" height="50" rx="3" fill="#0a0a0a"/>`;
  if (pantalla === 'azul') dentro = `<rect x="36" y="46" width="80" height="50" rx="3" fill="#1179d4"/>
      <circle cx="64" cy="71" r="6.5" fill="#fff"/><rect x="74" y="67.5" width="22" height="7" rx="3.5" fill="#fff"/>`;
  if (pantalla === 'negra') dentro = `<rect x="36" y="46" width="80" height="50" rx="3" fill="url(#retro)"/>`;
  if (pantalla === 'menu') {
    dentro = `<rect x="36" y="46" width="80" height="50" rx="3" fill="#000"/>
      <rect x="41" y="51" width="34" height="3" rx="1" fill="#f0a030"/>`;
    for (let i = 0; i < 6; i++) dentro += `<rect x="41" y="${59 + i * 6}" width="${[46, 52, 44, 50, 40, 36][i]}" height="3" rx="1" fill="${i === 1 ? '#fff' : '#4aa3ff'}"/>`;
    dentro += `<rect x="39" y="63.5" width="58" height="6" rx="1.5" fill="none" stroke="${A}" stroke-width="1.2"/>`;
  }

  return `<svg width="${ancho}" height="${ancho * 128 / 190}" viewBox="0 0 190 128" xmlns="http://www.w3.org/2000/svg">
    <defs>
      <filter id="luz" x="-60%" y="-60%" width="220%" height="220%"><feGaussianBlur stdDeviation="2.2" result="b"/>
        <feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
      <radialGradient id="retro" cx="50%" cy="50%" r="65%"><stop offset="0" stop-color="#3a3d42"/><stop offset="1" stop-color="#1e2023"/></radialGradient>
    </defs>
    <!-- Volumen, en el canto de arriba -->
    <rect x="86" y="21" width="14" height="6" rx="2" ${boton(vol)} stroke-width="1" ${brillo(vol)}/>
    <rect x="104" y="21" width="14" height="6" rx="2" ${boton(vol)} stroke-width="1" ${brillo(vol)}/>
    <text x="102" y="15" font-size="9" fill="${vol === 'suelto' ? '#8b857d' : A}" text-anchor="middle" font-weight="${vol === 'suelto' ? 500 : 700}">${
      { suelto: 'Volumen', pulsado: 'Vol + y Vol −', una: 'Vol +: una vez' }[vol]}</text>
    <!-- Cuerpo y pantalla -->
    <path d="M30 26 H122 Q130 26 130.5 34 L133.5 104 Q134 112 126 112 H26 Q18 112 18.5 104 L21.5 34 Q22 26 30 26 Z" fill="#2c2a27" stroke="#ddd6ca" stroke-width="2.2" stroke-linejoin="round"/>
    ${dentro}
    <path d="M30 103 H122 M30 107 H122" stroke="#55504a" stroke-width="1.6" stroke-linecap="round"/>
    <!-- Encendido, detrás: el botón blanco junto a la toma de corriente -->
    <circle cx="163" cy="80" r="17" fill="#1b1a18" stroke="${enc === 'suelto' ? GRIS2 : A}" stroke-width="1.5" stroke-dasharray="${enc === 'suelto' ? '3 3' : '0'}"/>
    <circle cx="156" cy="80" r="4.5" fill="#111" stroke="#77716a" stroke-width="1.2"/><circle cx="156" cy="80" r="1.3" fill="#77716a"/>
    <circle cx="170" cy="80" r="5.5" fill="${enc === 'suelto' ? '#e9e5dd' : '#fff'}" stroke="${enc === 'suelto' ? GRIS2 : A}" stroke-width="${enc === 'suelto' ? 1 : 2.4}" ${brillo(enc)}/>
    <text x="163" y="108" font-size="9" fill="${enc === 'suelto' ? '#8b857d' : A}" text-anchor="middle" font-weight="600">Encendido</text>
    <text x="163" y="118" font-size="8" fill="#8b857d" text-anchor="middle">(detrás)</text>
    ${enc === 'pulsado' ? `<text x="163" y="56" font-size="9" fill="${A}" text-anchor="middle" font-weight="700">mantén</text>` : ''}
  </svg>`;
}

function tarjeta({ num, tit, det, clave = false, dibujo }) {
  return `<div class="tarjeta${clave ? ' clave' : ''}"><div class="num">${num}</div>${dibujo}<div class="tit">${tit}</div><div class="det">${det}</div></div>`;
}
