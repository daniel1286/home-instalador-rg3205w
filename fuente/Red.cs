// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace HomeInstalador
{
    static class Red
    {
        const string Agente = "Instalar-Home";

        public static string Texto(string url)
        {
            using (var w = new WebClient())
            {
                w.Headers[HttpRequestHeader.UserAgent] = Agente;
                w.Encoding = Encoding.UTF8;
                return w.DownloadString(url);
            }
        }

        // Las versiones de Home que se pueden instalar, con su zip, tamaño, huella y notas. Las da el servidor de Home,
        // que las saca de las Releases de la ROM: si un día los paquetes cambian de sitio, cambia el servidor y no este
        // .exe. La descarga va directa a donde diga cada versión y su huella se comprueba igual
        public static List<Release> Versiones(string direccion = Rutas.Versiones)
        {
            var req = (HttpWebRequest)WebRequest.Create(direccion);
            req.UserAgent = Agente;
            req.Accept = "application/json";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            string json;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = r.ReadToEnd();
            return LeerVersiones(json);
        }

        static readonly Regex NumeroValido = new Regex(@"^[0-9A-Za-z._-]{1,32}$");
        static readonly Regex ZipValido = new Regex(@"^Home-[0-9A-Za-z._-]+-RG3205W\.zip$");
        static readonly Regex HuellaValida = new Regex("^[0-9a-f]{64}$");
        static readonly Regex SitioValido = new Regex("^[0-9A-Za-z][0-9A-Za-z .-]{0,39}$");

        // Solo entra lo que se puede usar tal cual: número y zip sin rutas, direcciones https y huella SHA256
        public static List<Release> LeerVersiones(string json)
        {
            var lector = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var raiz = lector.DeserializeObject(json) as Dictionary<string, object>;
            if (raiz == null || Cadena(raiz, "v") != "1" || Cadena(raiz, "dispositivo") != "rg3205w")
                throw new Exception("La lista de versiones no tiene el formato esperado.");
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

        // Descarga con reanudación: lo bajado queda en .part y la siguiente vez sigue desde ahí
        public static void Bajar(string url, string destino, Estado e)
        {
            string parte = destino + ".part";
            long ya = File.Exists(parte) ? new FileInfo(parte).Length : 0;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Agente;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            if (ya > 0) req.AddRange(ya);

            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException ex)
            {
                // 416: lo que hay en .part ya es el archivo entero
                var r = ex.Response as HttpWebResponse;
                if (ya > 0 && r != null && (int)r.StatusCode == 416) { r.Close(); Mover(parte, destino); return; }
                throw;
            }

            using (resp)
            {
                if (ya > 0 && resp.StatusCode != HttpStatusCode.PartialContent) ya = 0;
                if (ya > 0) e.Anotar(string.Format("  Sigue la descarga desde {0} MB.", ya / 1048576));
                long total = ya + Math.Max(0L, resp.ContentLength);
                e.Total = total;
                e.Bytes = ya;
                using (var sal = new FileStream(parte, ya > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                using (var ent = resp.GetResponseStream())
                {
                    var buf = new byte[1 << 20];
                    var marca = Stopwatch.StartNew();
                    long desde = ya;
                    int n;
                    while ((n = ent.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (e.Cancelar) throw new Exception(Detenida);
                        sal.Write(buf, 0, n);
                        ya += n;
                        e.Bytes = ya;
                        if (total > 0) e.Progreso = 100.0 * ya / total;
                        if (marca.Elapsed.TotalSeconds >= 1)
                        {
                            e.Velocidad = (ya - desde) / marca.Elapsed.TotalSeconds;
                            desde = ya;
                            marca.Restart();
                        }
                    }
                }
                if (total > 0 && ya < total)
                    throw new Exception("La descarga se ha cortado. Vuelve a pulsar Descargar: sigue donde se quedó.");
            }
            Mover(parte, destino);
        }

        public const string Detenida = "Descarga detenida. Lo bajado se guarda y sigue la próxima vez.";

        static void Mover(string de, string a)
        {
            if (File.Exists(a)) File.Delete(a);
            File.Move(de, a);
        }
    }
}
