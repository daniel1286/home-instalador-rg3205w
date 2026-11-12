// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

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
        // buscando | ok | bloqueada | no_valido | varios | ninguno | sin_driver | edl | sin_fastboot | error
        public readonly string Estado;
        public string Serie = "", Variante = "", Texto = "";
        public Aparato(string estado) { Estado = estado; }
    }

    // Un paso de un trabajo: espera | en_curso | hecho | fallo
    class Fase
    {
        public readonly string Nombre;
        public volatile string Estado = "espera";
        public Fase(string nombre) { Nombre = nombre; }
    }

    // Estado compartido entre la ventana y los hilos de trabajo
    class Estado
    {
        readonly object cerrojo = new object();

        public volatile string FB;
        public volatile bool Cerrar, Cancelar, Releer;
        public volatile Aparato Aparato = new Aparato("buscando");
        // Lista de versiones: buscando | ok | sin_red
        public volatile string GitHub = "buscando", ErrorGitHub = "";
        public volatile List<Release> Releases = new List<Release>();

        // Trabajo en marcha: "" | descargar | driver | desbloquear | instalar
        public volatile string Trabajo = "";
        // Cómo acabó: "" | en_curso | hecho | fallo | visto
        public volatile string Fase = "";
        public volatile string Texto = "", Error = "";
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
            Trabajo = trabajo; Fase = "en_curso"; Error = ""; Texto = "";
            Progreso = -1; Bytes = 0; Total = 0; Velocidad = 0; Cancelar = false;
        }

        public void NuevasFases(params string[] nombres)
        {
            var l = new List<Fase>();
            foreach (string n in nombres) l.Add(new Fase(n));
            Pasos = l;
        }
    }
}
