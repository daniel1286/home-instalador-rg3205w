// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace HomeInstalador
{
    // Direcciones de internet y carpetas del PC que usa el instalador
    static class Rutas
    {
        public const string Repo = "daniel1286/home-rom-rg3205w";
        public const string Web = "https://github.com/" + Repo;
        public const string GuiaWeb = Web + "/blob/main/guias/INSTALAR.md";
        public const string Crudo = "https://raw.githubusercontent.com/" + Repo + "/main";
        public const string Zry = "https://raw.githubusercontent.com/zry98/movistar-home-hacks/main/assets/img";
        public const string Demo = "https://daniel1286.github.io/home-rom-rg3205w/";
        public const string Incidencias = "https://github.com/daniel1286/home-instalador-rg3205w/issues/new/choose";
        // Servidor de Home: da la lista de versiones y recibe el recuento y los informes de fallos (docs/estadisticas.md
        // del repo de la ROM). Si los paquetes cambian de sitio, cambia el servidor y no este .exe
        public const string Servidor = "https://home-rom.home1286.workers.dev";
        public const string Versiones = Servidor + "/v1/instalar/rg3205w.json";
        public const string UrlHerramientas = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
        public const string UrlDriver = "https://dl.google.com/android/repository/usb_driver_r13-windows.zip";

        // El .exe se puede abrir desde Descargas: lo suyo va a una carpeta propia y no junto a él
        public static readonly string Datos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Home", "Instalador");
        public static readonly string Herramientas = Path.Combine(Datos, "herramientas");
        public static readonly string Registros = Path.Combine(Datos, "registros");
        public static readonly string Letras = Path.Combine(Datos, "letras");
        static readonly string Ajustes = Path.Combine(Datos, "carpeta_descargas.txt");

        // Carpeta de descargas: la elegida la última vez o Descargas\Home
        public static string Descargas()
        {
            try
            {
                if (File.Exists(Ajustes))
                {
                    string c = File.ReadAllText(Ajustes).Trim();
                    if (c.Length > 0) return c;
                }
            }
            catch { }
            string usuario = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string bajadas = Path.Combine(usuario, "Downloads");
            return Path.Combine(Directory.Exists(bajadas) ? bajadas : Datos, "Home");
        }

        public static void GuardarDescargas(string carpeta)
        {
            try
            {
                Directory.CreateDirectory(Datos);
                File.WriteAllText(Ajustes, carpeta);
            }
            catch { }
        }

        // Las rutas del PC salen como %USERPROFILE%: así un registro se puede compartir sin el nombre de usuario
        public static string SinUsuario(string texto)
        {
            string usuario = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(usuario) || texto == null) return texto;
            int i;
            while ((i = texto.IndexOf(usuario, StringComparison.OrdinalIgnoreCase)) >= 0)
                texto = texto.Substring(0, i) + "%USERPROFILE%" + texto.Substring(i + usuario.Length);
            return texto;
        }
    }
}
