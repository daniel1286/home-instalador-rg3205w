// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HomeInstalador
{
    // Una versión de Home de la lista del servidor (sale de una Release de la ROM)
    class Release
    {
        public string Numero, Fecha, Notas, Zip, Url, Sums, Huella;
        // De dónde sale de verdad el archivo («GitHub»): la descarga puede pasar antes por el servidor de Home
        public string Sitio;
        public bool Previa;
        public long Tamano;
    }

    // Lo que se sabe del aparato conectado. Se sustituye entero, nunca se cambia por partes
    class Aparato
    {
        // buscando | ok | bloqueada | no_valido | varios | ninguno | sin_driver | edl | usb_mal | sin_fastboot | error
        public readonly string Estado;
        public string Serie = "", Variante = "", Texto = "";
        public Aparato(string estado) { Estado = estado; }
    }

    // Un paso de un trabajo: espera | en_curso | hecho | fallo
    class Fase
    {
        public readonly string Nombre, Texto;
        public volatile string Estado = "espera";
        public Fase(string nombre, string texto) { Nombre = nombre; Texto = texto; }
    }

    // Lo que la persona ha pedido parar (detener la descarga, cancelar el desbloqueo, cerrar la ventana): no es un fallo
    class Cancelacion : Exception
    {
        public Cancelacion(string mensaje) : base(mensaje) { }
    }

    // Un fallo con su causa conocida. Motivo es la clase que va al registro y, si el servidor la admite, a la estadística
    class Fallo : Exception
    {
        public readonly string Motivo;
        public Fallo(string motivo, string mensaje) : base(mensaje) { Motivo = motivo; }
    }

    // Estado compartido entre la ventana y los hilos de trabajo
    class Estado
    {
        readonly object cerrojo = new object();

        // fastboot propio listo para usar, o null. Herr dice por qué falta: falta | quitado | danado | antiguo
        public volatile string FB, Herr = "falta";
        public volatile bool Cerrar, Cancelar, Releer;
        public volatile Aparato Aparato = new Aparato("buscando");
        // Lista de versiones: buscando | ok | sin_red. ErrorGitHub es la causa, contada para la persona
        public volatile string GitHub = "buscando", ErrorGitHub = "", FuenteVersiones = "";
        public volatile List<Release> Releases = new List<Release>();

        // Trabajo en marcha: "" | descargar | driver | desbloquear | instalar
        public volatile string Trabajo = "";
        // Cómo acabó: "" | en_curso | hecho | fallo | visto
        public volatile string Fase = "";
        // Motivo: "" | cancelado (lo ha parado la persona) | la clase del fallo (usb, ocupado, espacio, red…)
        public volatile string Texto = "", Error = "", Motivo = "";
        public volatile List<Fase> Pasos;

        public readonly ConcurrentQueue<string> Log = new ConcurrentQueue<string>();

        double progreso = -1, velocidad;
        long bytes, total;
        public double Progreso { get { lock (cerrojo) return progreso; } set { lock (cerrojo) progreso = value; } }
        public double Velocidad { get { lock (cerrojo) return velocidad; } set { lock (cerrojo) velocidad = value; } }
        public long Bytes { get { lock (cerrojo) return bytes; } set { lock (cerrojo) bytes = value; } }
        public long Total { get { lock (cerrojo) return total; } set { lock (cerrojo) total = value; } }

        public void Anotar(string t) { Log.Enqueue(t); }
        public void Contar(string t) { Texto = t; Anotar(t); }

        public void Empezar(string trabajo)
        {
            Trabajo = trabajo; Fase = "en_curso"; Error = ""; Motivo = ""; Texto = "";
            Progreso = -1; Bytes = 0; Total = 0; Velocidad = 0; Cancelar = false;
        }

        // Nombre de cada paso tal como se ve en la pantalla (el resto se ve con su nombre: boot, system, cache…)
        static readonly Dictionary<string, string> TextoFase = new Dictionary<string, string>
        {
            { "herramientas", "herramientas" }, { "descargar", "descarga" }, { "zip", "huella del zip" },
            { "descomprimir", "descomprimir" }, { "imagenes", "comprobar imágenes" }, { "askey", "Askey" }
        };

        public void NuevasFases(params string[] nombres)
        {
            var l = new List<Fase>();
            foreach (string n in nombres)
            {
                string t;
                l.Add(new Fase(n, TextoFase.TryGetValue(n, out t) ? t : n));
            }
            Pasos = l;
        }
    }
}
