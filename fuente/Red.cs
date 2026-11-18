// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace HomeInstalador
{
    static class Red
    {
        const string Agente = "Instalar-Home";

        // Con un proxy de empresa que pide usuario, se usa el de la sesión de Windows
        public static void Preparar()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var proxy = WebRequest.DefaultWebProxy;
            if (proxy != null) proxy.Credentials = CredentialCache.DefaultNetworkCredentials;
        }

        static HttpWebRequest Peticion(string url, int espera)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Agente;
            req.Timeout = espera;
            req.ReadWriteTimeout = espera;
            return req;
        }

        public static string Texto(string url)
        {
            var req = Peticion(url, 20000);
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        // Un fallo de red contado para la persona, con la causa que se puede arreglar
        public static string Explicar(Exception ex)
        {
            var w = ex as WebException;
            if (w == null)
            {
                if (ex is IOException) return "Se ha cortado la conexión.";
                return ex.Message;
            }
            var r = w.Response as HttpWebResponse;
            int codigo = r != null ? (int)r.StatusCode : 0;
            switch (w.Status)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    return "No se encuentra el servidor (fallo de DNS). Revisa la conexión; un filtro de contenidos o la red de una empresa pueden bloquearlo.";
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "No se encuentra el proxy que tiene configurado Windows.";
                case WebExceptionStatus.ConnectFailure:
                    return "No se puede conectar con el servidor: la red o un cortafuegos lo impiden.";
                case WebExceptionStatus.Timeout:
                    return "El servidor no contesta.";
                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    return "No se puede abrir una conexión segura. Suele ser la fecha y hora del PC mal puestas, o un antivirus o proxy que revisa las conexiones.";
                case WebExceptionStatus.ProtocolError:
                    if (codigo == 407) return "El proxy de esta red pide usuario y contraseña.";
                    if (codigo == 404 || codigo == 410) return "El archivo ya no está en el servidor (" + codigo + ").";
                    if (codigo == 403) return "El servidor no deja descargarlo (403).";
                    if (codigo == 429) return "Demasiadas peticiones seguidas: espera unos minutos.";
                    if (codigo >= 500) return "El servidor tiene un problema ahora mismo (" + codigo + ").";
                    return "El servidor ha contestado con un error (" + codigo + ").";
                default:
                    return "Se ha cortado la conexión.";
            }
        }

        // Los fallos que pueden ir mejor al reintentar
        static bool Reintentable(Exception ex)
        {
            if (Imagenes.DiscoLleno(ex)) return false;
            var w = ex as WebException;
            if (w == null) return ex is IOException || ex is Cortada;
            var r = w.Response as HttpWebResponse;
            int c = r != null ? (int)r.StatusCode : 0;
            return w.Status != WebExceptionStatus.ProtocolError || c == 408 || c == 429 || c >= 500;
        }

        // ------------------------------------------------------------ lista de versiones

        // Las versiones de Home que se pueden instalar, con su zip, tamaño, huella y notas. Las da el servidor de Home,
        // que las saca de las Releases de la ROM: si un día los paquetes cambian de sitio, cambia el servidor y no este
        // .exe. Si el servidor no contesta (bloqueado por la red, caído), se leen las Releases de GitHub con las mismas
        // reglas. fuente dice de dónde ha salido; fallo, por qué no del servidor
        public static List<Release> Versiones(out string fuente, out string fallo)
        {
            fallo = "";
            try
            {
                fuente = "servidor";
                var req = Peticion(Rutas.Versiones, 20000);
                req.Accept = "application/json";
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return LeerVersiones(r.ReadToEnd());
            }
            catch (Exception ex)
            {
                if (!(ex is WebException || ex is IOException || ex is FormatException)) throw;
                fallo = ex is FormatException ? ex.Message : Explicar(ex);
            }
            fuente = "GitHub";
            Estadistica.Acepta = new HashSet<string>();
            try
            {
                var req = Peticion(Rutas.ReleasesApi, 20000);
                req.Accept = "application/vnd.github+json";
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return DeGitHub(r.ReadToEnd());
            }
            catch (WebException ex2) { throw new Fallo("sin_red", fallo + " GitHub tampoco contesta: " + Explicar(ex2)); }
            catch (IOException ex2) { throw new Fallo("sin_red", fallo + " GitHub tampoco contesta: " + Explicar(ex2)); }
        }

        static readonly Regex NumeroValido = new Regex(@"^[0-9A-Za-z._-]{1,32}$");
        static readonly Regex ZipValido = new Regex(@"^Home-[0-9A-Za-z._-]+-RG3205W\.zip$");
        static readonly Regex HuellaValida = new Regex("^[0-9a-f]{64}$");
        static readonly Regex SitioValido = new Regex("^[0-9A-Za-z][0-9A-Za-z .-]{0,39}$");

        static Dictionary<string, object> Objeto(string json)
        {
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        // Solo entra lo que se puede usar tal cual: número y zip sin rutas, direcciones https y huella SHA256
        public static List<Release> LeerVersiones(string json)
        {
            var raiz = Objeto(json);
            if (raiz == null || Cadena(raiz, "v") != "1" || Cadena(raiz, "dispositivo") != "rg3205w")
                throw new FormatException("La lista de versiones del servidor de Home no tiene el formato esperado.");
            // Lo que el servidor admite de más en las estadísticas (abandono, motivo): solo se envía lo que anuncia
            var acepta = new HashSet<string>();
            object a;
            if (raiz.TryGetValue("acepta", out a) && a is object[])
                foreach (object x in (object[])a) if (x is string) acepta.Add((string)x);
            Estadistica.Acepta = acepta;
            // Control de despliegue: si el servidor pide un instalador más nuevo que este, no se instala con uno viejo.
            // Solo frena; nunca decide qué se graba. Si el servidor no contesta, no hay mínimo y el .exe sigue valiendo.
            string minimo = Cadena(raiz, "instalador_minimo");
            if (minimo != "" && !AlMenos(Estadistica.Instalador, minimo))
                throw new Fallo("instalador_viejo",
                    "Hay una versión más nueva del instalador. Esta es la " + Estadistica.Instalador + " y hace falta al menos la " + minimo +
                    ". Descarga la última en " + Rutas.InstaladorReleases + " y vuelve a abrirla.");
            var lista = new List<Release>();
            var versiones = raiz.ContainsKey("versiones") ? raiz["versiones"] as object[] : null;
            foreach (object o in versiones ?? new object[0])
            {
                var x = o as Dictionary<string, object>;
                if (x == null) continue;
                string numero = Cadena(x, "numero"), zip = Cadena(x, "zip"), url = Cadena(x, "url");
                string sums = Cadena(x, "sums"), huella = Cadena(x, "huella");
                if (!NumeroValido.IsMatch(numero) || !ZipValido.IsMatch(zip) || !Https(url)) continue;
                if ((sums != "" && !Https(sums)) || (huella != "" && !HuellaValida.IsMatch(huella))) continue;
                long tamano;
                if (!long.TryParse(Cadena(x, "tamano"), out tamano) || tamano < 0) tamano = 0;
                string sitio = Cadena(x, "sitio");
                lista.Add(new Release
                {
                    Numero = numero, Fecha = Cadena(x, "fecha"), Notas = Cadena(x, "notas"), Previa = Si(x, "previa"),
                    Zip = zip, Url = url, Tamano = tamano, Sums = sums, Huella = huella,
                    Sitio = SitioValido.IsMatch(sitio) ? sitio : Sitio(url)
                });
            }
            return lista;
        }

        // Las Releases de la API de GitHub, con las mismas reglas que el servidor: publicadas, con el zip del RG3205W y
        // solo con direcciones de descarga de las Releases de la ROM
        public static List<Release> DeGitHub(string json)
        {
            object[] releases;
            try { releases = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as object[]; }
            catch (ArgumentException) { releases = null; }
            if (releases == null) throw new FormatException("GitHub no ha devuelto una lista de versiones.");
            string descarga = "https://github.com/" + Rutas.Repo + "/releases/download/";
            var lista = new List<Release>();
            foreach (object o in releases)
            {
                var r = o as Dictionary<string, object>;
                object borrador;
                if (r == null || !r.TryGetValue("draft", out borrador) || !(borrador is bool) || (bool)borrador) continue;
                string numero = Cadena(r, "tag_name");
                if (!NumeroValido.IsMatch(numero)) continue;
                var adjuntos = (r.ContainsKey("assets") ? r["assets"] as object[] : null) ?? new object[0];
                Func<Dictionary<string, object>, bool> deAqui = x => Cadena(x, "browser_download_url").StartsWith(descarga, StringComparison.Ordinal);
                var zip = adjuntos.OfType<Dictionary<string, object>>().FirstOrDefault(x => ZipValido.IsMatch(Cadena(x, "name")) && deAqui(x));
                if (zip == null) continue;
                var sums = adjuntos.OfType<Dictionary<string, object>>().FirstOrDefault(x => Cadena(x, "name") == "SHA256SUMS" && deAqui(x));
                long tamano;
                if (!long.TryParse(Cadena(zip, "size"), out tamano) || tamano < 0) tamano = 0;
                var huella = Regex.Match(Cadena(zip, "digest"), "^sha256:([0-9a-f]{64})$");
                string notas = Cadena(r, "body");
                lista.Add(new Release
                {
                    Numero = numero, Fecha = Cadena(r, "published_at"), Notas = notas.Length > 20000 ? notas.Substring(0, 20000) : notas,
                    Previa = Si(r, "prerelease"), Zip = Cadena(zip, "name"), Url = Cadena(zip, "browser_download_url"), Tamano = tamano,
                    Sums = sums != null ? Cadena(sums, "browser_download_url") : "", Huella = huella.Success ? huella.Groups[1].Value : "",
                    Sitio = "GitHub"
                });
            }
            return lista;
        }

        static bool Https(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && u.Scheme == Uri.UriSchemeHttps;
        }

        // De dónde se descarga: «GitHub» o el nombre del sitio
        public static string Sitio(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out u)) return "internet";
            return u.Host == "github.com" ? "GitHub" : u.Host;
        }

        public static string Sitio(Release r)
        {
            return string.IsNullOrEmpty(r.Sitio) ? Sitio(r.Url) : r.Sitio;
        }

        // La descarga que pasa por el servidor de Home se cuenta allí; con «Contar esta instalación» desmarcada se le pide
        // que no la cuente (contar=0) y solo hace de puerta hacia GitHub
        public static string Contada(string url, bool contar)
        {
            Uri u, servidor = new Uri(Rutas.Servidor);
            if (contar || !Uri.TryCreate(url ?? "", UriKind.Absolute, out u) || u.Host != servidor.Host) return url;
            return url + (u.Query.Length > 0 ? "&" : "?") + "contar=0";
        }

        static string Cadena(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? v.ToString() : "";
        }

        static bool Si(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v is bool && (bool)v;
        }

        // Compara versiones «X.Y» o «X.Y.Z»: ¿es `tengo` al menos `minimo`? Si alguna no tiene ese formato, no frena (true)
        static readonly Regex VersionPartes = new Regex(@"^(\d{1,2})\.(\d{1,2})(?:\.(\d{1,2}))?$");
        static int[] Partes(string v)
        {
            if (v == null) return null;
            var m = VersionPartes.Match(v);
            if (!m.Success) return null;
            return new[] { int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0 };
        }
        static bool AlMenos(string tengo, string minimo)
        {
            int[] a = Partes(tengo), b = Partes(minimo);
            if (a == null || b == null) return true;
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            return true;
        }

        // ------------------------------------------------------------ descargas

        // El servidor ha cerrado antes de tiempo
        class Cortada : Exception
        {
            public Cortada() : base("Se ha cortado la conexión.") { }
        }

        public const string Detenida = "Descarga detenida. Lo bajado se guarda y sigue la próxima vez.";
        static readonly int[] Esperas = { 3, 8, 15, 30 };

        // Descarga con reanudación: lo bajado queda en .part y se sigue desde ahí. Si se corta, lo vuelve a intentar
        // solo, hasta cinco veces y esperando cada vez un poco más
        public static void Bajar(string url, string destino, Estado e)
        {
            for (int intento = 0; ; intento++)
            {
                try { BajarUnaVez(url, destino, e); return; }
                catch (Exception ex)
                {
                    if (ex is Cancelacion) throw;
                    if (Imagenes.DiscoLleno(ex)) throw Imagenes.FaltaEspacio(Path.GetDirectoryName(destino), 0, 0);
                    if (!Reintentable(ex)) throw new Fallo("red", Explicar(ex));
                    if (intento >= Esperas.Length)
                        throw new Fallo("red", Explicar(ex) + " Se ha intentado cinco veces. Revisa la conexión y vuelve a pulsar Descargar: sigue donde se quedó.");
                    int s = Esperas[intento];
                    e.Anotar(string.Format("  {0} Se vuelve a intentar en {1} s ({2} de 5)…", Explicar(ex), s, intento + 2));
                    var t = Stopwatch.StartNew();
                    while (t.Elapsed.TotalSeconds < s)
                    {
                        if (e.Cancelar || e.Cerrar) throw new Cancelacion(Detenida);
                        Thread.Sleep(200);
                    }
                }
            }
        }

        static void BajarUnaVez(string url, string destino, Estado e)
        {
            string parte = destino + ".part", marca = destino + ".etag";
            long ya = File.Exists(parte) ? new FileInfo(parte).Length : 0;
            var req = Peticion(url, 30000);
            req.ReadWriteTimeout = 60000;
            if (ya > 0)
            {
                req.AddRange(ya);
                // Si el archivo del servidor ya no es el mismo, If-Range hace que llegue entero en vez de pegar trozos
                string etiqueta = File.Exists(marca) ? File.ReadAllText(marca).Trim() : "";
                if (etiqueta.Length > 0) req.Headers["If-Range"] = etiqueta;
            }

            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException ex)
            {
                // 416: lo que hay en .part ya es el archivo entero
                var r = ex.Response as HttpWebResponse;
                if (ya > 0 && r != null && (int)r.StatusCode == 416) { r.Close(); Mover(parte, destino); Borrar(marca); return; }
                throw;
            }

            using (resp)
            {
                if (ya > 0 && resp.StatusCode != HttpStatusCode.PartialContent)
                {
                    ya = 0;
                    e.Anotar("  El archivo del servidor ha cambiado: se descarga desde el principio.");
                }
                else if (ya > 0) e.Anotar(string.Format("  Sigue la descarga desde {0} MB.", ya / 1048576));
                // If-Range solo vale con una etiqueta fuerte o con la fecha de modificación
                string etag = resp.Headers[HttpResponseHeader.ETag];
                if (etag != null && etag.StartsWith("W/")) etag = null;
                string nueva = etag ?? resp.Headers[HttpResponseHeader.LastModified] ?? "";
                if (ya == 0 || !File.Exists(marca))
                {
                    if (nueva.Length > 0) File.WriteAllText(marca, nueva);
                    else Borrar(marca);
                }
                long total = ya + Math.Max(0L, resp.ContentLength);
                e.Total = total;
                e.Bytes = ya;
                using (var sal = new FileStream(parte, ya > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                using (var ent = resp.GetResponseStream())
                {
                    var buf = new byte[1 << 20];
                    var reloj = Stopwatch.StartNew();
                    long desde = ya;
                    int n;
                    while ((n = ent.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (e.Cancelar || e.Cerrar) throw new Cancelacion(Detenida);
                        sal.Write(buf, 0, n);
                        ya += n;
                        e.Bytes = ya;
                        if (total > 0) e.Progreso = 100.0 * ya / total;
                        if (reloj.Elapsed.TotalSeconds >= 1)
                        {
                            e.Velocidad = (ya - desde) / reloj.Elapsed.TotalSeconds;
                            desde = ya;
                            reloj.Restart();
                        }
                    }
                }
                if (total > 0 && ya < total) throw new Cortada();
            }
            Mover(parte, destino);
            Borrar(marca);
        }

        static void Borrar(string archivo)
        {
            if (File.Exists(archivo)) File.Delete(archivo);
        }

        static void Mover(string de, string a)
        {
            if (File.Exists(a)) File.Delete(a);
            File.Move(de, a);
        }
    }
}
