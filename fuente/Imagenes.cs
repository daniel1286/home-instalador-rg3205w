// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HomeInstalador
{
    // Las imágenes de Home: dónde se graban, cuánto caben y cómo se comprueban
    static class Imagenes
    {
        public static readonly string[] Particiones = { "boot", "recovery", "vendor", "system", "splash" };

        // Tamaño de cada partición. De recovery solo valen los primeros 30 MiB: los 2 últimos están en el grupo
        // protegido contra escritura de devinfo, y ahí las escrituras se pierden sin dar error. Es la lista blanca: un
        // paquete puede grabar menos particiones o pedir menos sitio, nunca otras ni más
        public static readonly Dictionary<string, long> Maximo = new Dictionary<string, long>
        {
            { "boot", 33554432 }, { "recovery", 31457280 }, { "vendor", 402653184 },
            { "system", 2684354560 }, { "splash", 11534336 }
        };

        public const string Detenido = "Detenido.";

        public static string Sha(string ruta, Func<bool> cancelar = null, Action<double> progreso = null)
        {
            using (var sha = SHA256.Create())
            using (var f = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var buf = new byte[4 << 20];
                long hecho = 0, total = Math.Max(1L, f.Length);
                int n;
                while ((n = f.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelar != null && cancelar()) throw new Cancelacion(Detenido);
                    sha.TransformBlock(buf, 0, n, null, 0);
                    hecho += n;
                    if (progreso != null) progreso(100.0 * hecho / total);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                var sb = new StringBuilder(64);
                foreach (byte b in sha.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // Las particiones que graba un paquete. Si trae particiones.json, sus nombres y tamaños, siempre dentro de la
        // lista blanca; si no, las de siempre
        public static List<KeyValuePair<string, long>> DelPaquete(string carpeta)
        {
            string json = Path.Combine(carpeta, "particiones.json");
            var l = new List<KeyValuePair<string, long>>();
            if (!File.Exists(json))
            {
                foreach (string p in Particiones) l.Add(new KeyValuePair<string, long>(p, Maximo[p]));
                return l;
            }
            object[] lista = null;
            try
            {
                var raiz = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(json)) as Dictionary<string, object>;
                if (raiz != null && raiz.ContainsKey("particiones")) lista = raiz["particiones"] as object[];
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            if (lista == null || lista.Length == 0) throw new Fallo("paquete", "El paquete trae una lista de particiones que no se puede leer.");
            foreach (object o in lista)
            {
                var d = o as Dictionary<string, object>;
                object n, m;
                string nombre = d != null && d.TryGetValue("nombre", out n) ? n as string : null;
                long maximo;
                if (nombre == null || !Maximo.TryGetValue(nombre, out maximo) || l.Any(x => x.Key == nombre))
                    throw new Fallo("paquete", "El paquete pide grabar «" + nombre + "», que este instalador no graba nunca. Descarga Home desde la lista de versiones.");
                if (d.TryGetValue("maximo", out m))
                {
                    long pedido;
                    if (m == null || !long.TryParse(m.ToString(), out pedido) || pedido <= 0)
                        throw new Fallo("paquete", "El paquete trae un tamaño no válido para " + nombre + ".");
                    maximo = Math.Min(maximo, pedido);
                }
                l.Add(new KeyValuePair<string, long>(nombre, maximo));
            }
            return l;
        }

        // Comprueba el SHA256 (el de su .sha256) y el tamaño de una imagen
        public static void Comprobar(string carpeta, string particion, long maximo, Func<bool> cancelar)
        {
            string img = Path.Combine(carpeta, particion + ".img");
            string nombre = particion + ".img";
            if (!File.Exists(img)) throw new Fallo("paquete", "Falta " + nombre + " en la carpeta de Home. Vuelve a descargarlo.");
            if (!File.Exists(img + ".sha256")) throw new Fallo("paquete", "Falta " + nombre + ".sha256 en la carpeta de Home. Vuelve a descargarlo.");
            string[] partes = File.ReadAllText(img + ".sha256").Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string esperado = partes.Length > 0 ? partes[0].ToLowerInvariant() : "";
            if (Sha(img, cancelar) != esperado)
                throw new Fallo("danado", nombre + " está dañado (su huella no coincide). Vuelve a descargar Home.");
            long tam = new FileInfo(img).Length;
            if (tam > maximo)
                throw new Fallo("tamano", string.Format("{0} ({1}) no cabe en {2} ({3}). Este paquete no es para este aparato.", nombre, TamanoTexto(tam), particion, TamanoTexto(maximo)));
        }

        public static bool CarpetaLista(string carpeta)
        {
            try
            {
                foreach (var p in DelPaquete(carpeta))
                {
                    string img = Path.Combine(carpeta, p.Key + ".img");
                    if (!File.Exists(img) || !File.Exists(img + ".sha256")) return false;
                }
                return true;
            }
            catch (Fallo) { return false; }
        }

        // La carpeta de dentro del zip (Home-2.1.1-RG3205W): de ahí sale la versión, no del nombre del archivo, que el
        // navegador puede haber cambiado a «Home-2.1.1-RG3205W (1).zip»
        public static string RaizDelZip(string zip)
        {
            using (var z = ZipFile.OpenRead(zip))
            {
                string raiz = null;
                foreach (var e in z.Entries)
                {
                    int barra = e.FullName.IndexOf('/');
                    if (barra <= 0) return null;
                    string r = e.FullName.Substring(0, barra);
                    if (raiz == null) raiz = r;
                    else if (raiz != r) return null;
                }
                return raiz != null && Regex.IsMatch(raiz, @"^Home-[0-9A-Za-z._-]{1,32}-RG3205W$") ? raiz : null;
            }
        }

        public static string NumeroDeCarpeta(string nombre)
        {
            var m = Regex.Match(nombre ?? "", @"^Home-([0-9A-Za-z._-]{1,32})-RG3205W$");
            return m.Success ? m.Groups[1].Value : null;
        }

        // Lo que ocupa todo lo de dentro del zip, leído de su índice
        public static long Descomprimido(string zip)
        {
            using (var z = ZipFile.OpenRead(zip)) return z.Entries.Sum(e => e.Length);
        }

        // Espacio libre en el disco de una carpeta (o de la primera de sus carpetas de arriba que exista)
        public static long Libre(string carpeta)
        {
            return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(carpeta))).AvailableFreeSpace;
        }

        // Hace falta «necesario» libre en el disco de «carpeta»: si no, un Fallo de espacio con lo que hay y lo que falta
        public static void HaceFalta(string carpeta, long necesario)
        {
            long libre;
            try { libre = Libre(carpeta); }
            catch (IOException) { return; }
            catch (ArgumentException) { return; }
            if (libre < necesario) throw FaltaEspacio(carpeta, necesario, libre);
        }

        public static Fallo FaltaEspacio(string carpeta, long necesario, long libre)
        {
            string disco = Path.GetPathRoot(Path.GetFullPath(carpeta)).TrimEnd('\\');
            return new Fallo("espacio", necesario > 0
                ? string.Format("Falta espacio en el disco {0}: hacen falta unos {1} y hay {2}. Libera espacio o cambia la carpeta de descargas.", disco, TamanoTexto(necesario), TamanoTexto(libre))
                : "El disco " + disco + " se ha llenado. Libera espacio o cambia la carpeta de descargas.");
        }

        // Disco lleno (ERROR_DISK_FULL o ERROR_HANDLE_DISK_FULL)
        public static bool DiscoLleno(Exception ex)
        {
            int h = ex is IOException ? ex.HResult & 0xFFFF : 0;
            return h == 112 || h == 39;
        }

        // Descomprime comprobando que nada sale de la carpeta de destino
        public static void Descomprimir(string zip, string destino, Action<double> progreso, Func<bool> cancelar)
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
                        throw new Fallo("paquete", "El zip trae una ruta no válida: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(ruta); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(ruta));
                    using (var ent = e.Open())
                    using (var sal = File.Create(ruta))
                    {
                        int n;
                        while ((n = ent.Read(buf, 0, buf.Length)) > 0)
                        {
                            if (cancelar != null && cancelar()) throw new Cancelacion(Detenido);
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
