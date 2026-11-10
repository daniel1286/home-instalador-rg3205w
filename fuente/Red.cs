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

        // Las releases de Home que traen el zip de instalación
        public static List<Release> Releases(string repo)
        {
            var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + repo + "/releases?per_page=20");
            req.UserAgent = Agente;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 20000;
            string json;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = r.ReadToEnd();

            var lector = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var lista = new List<Release>();
            var todas = lector.DeserializeObject(json) as object[];
            if (todas == null) return lista;
            foreach (object o in todas)
            {
                var x = o as Dictionary<string, object>;
                if (x == null || Si(x, "draft")) continue;
                Dictionary<string, object> zip = null, sums = null;
                var adjuntos = x.ContainsKey("assets") ? x["assets"] as object[] : null;
                foreach (object a in adjuntos ?? new object[0])
                {
                    var d = a as Dictionary<string, object>;
                    if (d == null) continue;
                    string nombre = Cadena(d, "name");
                    if (zip == null && Regex.IsMatch(nombre, @"^Home-.+-RG3205W\.zip$")) zip = d;
                    if (sums == null && nombre == "SHA256SUMS") sums = d;
                }
                if (zip == null) continue;
                lista.Add(new Release
                {
                    Numero = Cadena(x, "tag_name"),
                    Fecha = Cadena(x, "published_at"),
                    Notas = Cadena(x, "body"),
                    Previa = Si(x, "prerelease"),
                    Zip = Cadena(zip, "name"),
                    Url = Cadena(zip, "browser_download_url"),
                    Tamano = zip.ContainsKey("size") ? Convert.ToInt64(zip["size"]) : 0,
                    Sums = sums != null ? Cadena(sums, "browser_download_url") : "",
                    Huella = Regex.Replace(Cadena(zip, "digest"), "^sha256:", "")
                });
            }
            return lista;
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
