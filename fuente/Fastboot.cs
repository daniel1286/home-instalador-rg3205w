// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace HomeInstalador
{
    class Fastboot
    {
        public class Resultado
        {
            public int Codigo;
            public string Salida, Error;
        }

        readonly string ruta;
        public Fastboot(string ruta) { this.ruta = ruta; }

        // El de las herramientas descargadas, el del PATH o el del SDK de Android
        public static string Buscar()
        {
            var candidatos = new List<string> { Path.Combine(Rutas.Herramientas, "platform-tools", "fastboot.exe") };
            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (d.Trim().Length > 0) candidatos.Add(Path.Combine(d.Trim(), "fastboot.exe")); }
                catch (ArgumentException) { }
            }
            candidatos.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk", "platform-tools", "fastboot.exe"));
            foreach (string c in candidatos) if (File.Exists(c)) return c;
            return null;
        }

        // Lanza fastboot sin ventana; cadaLinea recibe cada línea de stderr (donde fastboot cuenta lo que hace)
        public Resultado Lanzar(string[] args, Action<string> cadaLinea)
        {
            var partes = new List<string>();
            foreach (string a in args) partes.Add(a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a);
            var psi = new ProcessStartInfo(ruta, string.Join(" ", partes))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                var salida = p.StandardOutput.ReadToEndAsync();
                var err = new StringBuilder();
                string l;
                while ((l = p.StandardError.ReadLine()) != null)
                {
                    err.AppendLine(l);
                    if (cadaLinea != null) cadaLinea(l);
                }
                p.WaitForExit();
                return new Resultado { Codigo = p.ExitCode, Salida = salida.Result, Error = err.ToString() };
            }
        }

        public string Variable(string nombre)
        {
            var r = Lanzar(new[] { "getvar", nombre }, null);
            foreach (string l in r.Error.Split('\n'))
                if (l.StartsWith(nombre + ":")) return l.Substring(nombre.Length + 1).Trim();
            return "";
        }

        public string[] Aparatos()
        {
            var lista = new List<string>();
            foreach (string l in Lanzar(new[] { "devices" }, null).Salida.Split('\n'))
            {
                string[] c = l.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (c.Length > 0) lista.Add(c[0]);
            }
            return lista.ToArray();
        }
    }
}
