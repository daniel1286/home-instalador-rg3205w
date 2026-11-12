// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace HomeInstalador
{
    // Las imágenes de Home: dónde se graban, cuánto caben y cómo se comprueban
    static class Imagenes
    {
        public static readonly string[] Particiones = { "boot", "recovery", "vendor", "system", "splash" };

        // Tamaño de cada partición. De recovery solo valen los primeros 30 MiB: los 2 últimos están en el grupo
        // protegido contra escritura de devinfo, y ahí las escrituras se pierden sin dar error
        public static readonly Dictionary<string, long> Maximo = new Dictionary<string, long>
        {
            { "boot", 33554432 }, { "recovery", 31457280 }, { "vendor", 402653184 },
            { "system", 2684354560 }, { "splash", 11534336 }
        };

        public static string Sha(string ruta)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(ruta))
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(f)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // Comprueba el SHA256 (el de su .sha256) y el tamaño de una imagen
        public static void Comprobar(string carpeta, string particion)
        {
            string img = Path.Combine(carpeta, particion + ".img");
            string nombre = particion + ".img";
            if (!File.Exists(img)) throw new Exception("Falta " + nombre);
            if (!File.Exists(img + ".sha256")) throw new Exception("Falta " + nombre + ".sha256");
            string[] partes = File.ReadAllText(img + ".sha256").Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string esperado = partes.Length > 0 ? partes[0].ToLowerInvariant() : "";
            if (Sha(img) != esperado)
                throw new Exception("La huella SHA256 de " + nombre + " no coincide: el archivo está dañado. Vuelve a descargar Home.");
            long tam = new FileInfo(img).Length;
            if (tam > Maximo[particion])
                throw new Exception(string.Format("{0} ({1} bytes) no cabe en {2} ({3} bytes).", nombre, tam, particion, Maximo[particion]));
        }

        public static bool CarpetaLista(string carpeta)
        {
            foreach (string p in Particiones)
            {
                string img = Path.Combine(carpeta, p + ".img");
                if (!File.Exists(img) || !File.Exists(img + ".sha256")) return false;
            }
            return true;
        }

        // Descomprime comprobando que nada sale de la carpeta de destino
        public static void Descomprimir(string zip, string destino, Action<double> progreso)
        {
            string raiz = Path.GetFullPath(destino).TrimEnd('\\') + "\\";
            using (var z = ZipFile.OpenRead(zip))
            {
                long total = 0, hecho = 0;
                foreach (var e in z.Entries) total += e.Length;
                total = Math.Max(1L, total);
                var buf = new byte[4 << 20];
                foreach (var e in z.Entries)
                {
                    string ruta = Path.GetFullPath(Path.Combine(destino, e.FullName));
                    if (!ruta.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("El zip trae una ruta no válida: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(ruta); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(ruta));
                    using (var ent = e.Open())
                    using (var sal = File.Create(ruta))
                    {
                        int n;
                        while ((n = ent.Read(buf, 0, buf.Length)) > 0)
                        {
                            sal.Write(buf, 0, n);
                            hecho += n;
                            if (progreso != null) progreso(100.0 * hecho / total);
                        }
                    }
                }
            }
        }

        public static string TamanoTexto(double b)
        {
            if (b >= 1073741824) return (b / 1073741824).ToString("0.00") + " GB";
            return (b / 1048576).ToString("0") + " MB";
        }
    }
}
