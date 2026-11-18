// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;

namespace HomeInstalador
{
    // Direcciones de internet y carpetas del PC que usa el instalador
    static class Rutas
    {
        public const string Repo = "daniel1286/home-rom-rg3205w";
        public const string Web = "https://github.com/" + Repo;
        public const string GuiaWeb = Web + "/blob/main/guias/INSTALAR.md";
        public const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases?per_page=20";
        public const string Demo = "https://daniel1286.github.io/home-rom-rg3205w/";
        // Un solo sitio para las incidencias del instalador, desde la guía y desde el informe de un fallo
        public const string Incidencias = "https://github.com/daniel1286/home-instalador-rg3205w/issues/new/choose";
        // La última versión del propio instalador: si el servidor pide una más nueva, se manda aquí
        public const string InstaladorReleases = "https://github.com/daniel1286/home-instalador-rg3205w/releases/latest";
        // Quien documentó el hardware del RG3205W (revisiones de la placa y conector J4902)
        public const string Hardware = "https://github.com/zry98";
        // Servidor de Home: da la lista de versiones y recibe el recuento y los informes de fallos (docs/estadisticas.md
        // del repo de la ROM). Si los paquetes cambian de sitio, cambia el servidor y no este .exe
        public const string Servidor = "https://home-rom.home1286.workers.dev";
        public const string Versiones = Servidor + "/v1/instalar/rg3205w.json";
        public const string UrlHerramientas = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
        public const string UrlDriver = "https://dl.google.com/android/repository/usb_driver_r13-windows.zip";

        // El .exe se puede abrir desde Descargas: lo suyo va a una carpeta propia y no junto a él
        public static readonly string Datos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Home", "Instalador");
        // Las herramientas (platform-tools, usb_driver) van directas en la carpeta de datos, sin un nivel de más
        public static readonly string Herramientas = Datos;
        public static readonly string Registros = Path.Combine(Datos, "registros");
        public static readonly string Letras = Path.Combine(Datos, "letras");
        // Las imágenes son datos de trabajo: si Descargas está en OneDrive, van aquí, fuera de la sincronización
        public static readonly string DescargasLocales = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Home", "Descargas");
        static readonly string Ajustes = Path.Combine(Datos, "carpeta_descargas.txt");

        // Carpeta de descargas: la elegida la última vez o Descargas\Home (la carpeta Descargas de verdad, aunque se haya
        // movido), salvo que esté en OneDrive
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
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string bajadas = Sistema.CarpetaDescargas();
            if (string.IsNullOrEmpty(bajadas) || !Directory.Exists(bajadas) || Sistema.EnOneDrive(bajadas)) return DescargasLocales;
            return Path.Combine(bajadas, "Home");
        }

        // Otras carpetas donde puede haber un Home ya descargado por una versión anterior del instalador
        public static IEnumerable<string> DescargasAnteriores()
        {
            string bajadas = Sistema.CarpetaDescargas();
            if (!string.IsNullOrEmpty(bajadas)) yield return Path.Combine(bajadas, "Home");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Home");
            yield return DescargasLocales;
        }

        public static bool GuardarDescargas(string carpeta)
        {
            try
            {
                Directory.CreateDirectory(Datos);
                File.WriteAllText(Ajustes, carpeta);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
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
