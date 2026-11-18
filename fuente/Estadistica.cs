// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace HomeInstalador
{
    // «Contar esta instalación»: al acabar, si ha salido bien o en qué paso ha fallado (formato v1 de las estadísticas
    // de Home). Sin identificador y sin nada del PC ni de la Askey. Se envía en segundo plano: si no llega, la
    // instalación no se entera
    static class Estadistica
    {
        // La versión del instalador sale del propio .exe (AssemblyVersion): 2.1.0.0 → «2.1», 2.1.3.0 → «2.1.3»
        public static readonly string Instalador = DeEnsamblado();
        const string Direccion = Rutas.Servidor + "/v1/instalacion";
        static readonly string Ajuste = Path.Combine(Rutas.Datos, "contar_instalacion.txt");
        // Lo que el servidor acepta: X.Y o X.Y.Z
        static readonly Regex VersionValida = new Regex(@"^\d{1,2}\.\d{1,2}(\.\d{1,2})?$");

        // Lo que el servidor admite además del formato v1 (lo anuncia en la lista de versiones): «abandono» y «motivo»
        public static volatile HashSet<string> Acepta = new HashSet<string>();

        static string DeEnsamblado()
        {
            var v = typeof(Estadistica).Assembly.GetName().Version;
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }

        public static bool VersionPublicable(string numero) { return numero != null && VersionValida.IsMatch(numero); }

        // Marcada de serie. Si se desmarca, se recuerda en este PC
        public static bool Marcada
        {
            get
            {
                try { return !File.Exists(Ajuste) || File.ReadAllText(Ajuste).Trim() != "no"; }
                catch (IOException) { return true; }
                catch (UnauthorizedAccessException) { return true; }
            }
            set
            {
                try
                {
                    Directory.CreateDirectory(Rutas.Datos);
                    File.WriteAllText(Ajuste, value ? "si" : "no");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // Nombre fijo de cada paso, tal como lo lee el panel. Cada nombre es de un solo trabajo, así el panel sabe de qué
        // trabajo es cada fila: descargar (herramientas … comprobar_imagenes), driver (controladores), desbloquear
        // (desbloquear) e instalar (verificar_imagenes, conectar, bloqueada, grabar_*, formatear_*)
        static readonly Dictionary<string, string> Descarga = new Dictionary<string, string>
        {
            { "herramientas", "herramientas" }, { "descargar", "descargar" }, { "zip", "comprobar_zip" },
            { "descomprimir", "descomprimir" }, { "imagenes", "comprobar_imagenes" }
        };
        static readonly Dictionary<string, string> Instalacion = new Dictionary<string, string>
        {
            { "imagenes", "verificar_imagenes" }, { "boot", "grabar_boot" }, { "recovery", "grabar_recovery" },
            { "vendor", "grabar_vendor" }, { "system", "grabar_system" }, { "splash", "grabar_splash" },
            { "cache", "formatear_cache" }, { "userdata", "formatear_userdata" }
        };

        // El paso en el que ha fallado un trabajo
        public static string Paso(string trabajo, List<Fase> pasos, string motivo)
        {
            if (trabajo == "driver") return "controladores";
            if (trabajo == "desbloquear") return "desbloquear";
            var nombres = trabajo == "instalar" ? Instalacion : Descarga;
            if (pasos != null)
                foreach (var f in pasos)
                {
                    if (f.Estado != "fallo") continue;
                    if (f.Nombre == "askey") return motivo == "bloqueada" ? "bloqueada" : "conectar";
                    string n;
                    if (nombres.TryGetValue(f.Nombre, out n)) return n;
                }
            return trabajo == "instalar" ? "conectar" : trabajo;
        }

        // Clases de fallo que el panel sabe enseñar (solo se envían si el servidor las admite)
        static readonly HashSet<string> Motivos = new HashSet<string>
        {
            "usb", "ocupado", "bloqueada", "tamano", "herramientas", "aparato", "fastboot", "desconectada", "no_responde",
            "tiempo", "espacio", "red", "danado", "paquete", "driver", "sin_permiso", "conectar", "otro"
        };

        // resultado: ok | fallo | abandono (cerró la ventana sin instalar; paso = hasta dónde llegó)
        public static string Mensaje(string home, string resultado, string paso, long segundos, string motivo = null)
        {
            var d = new Dictionary<string, object>
            {
                { "v", 1 }, { "camino", "cable" }, { "instalador", Instalador }, { "home", home }, { "resultado", resultado }
            };
            if (resultado != "ok") d["paso"] = paso;
            if (resultado == "fallo" && motivo != null && Motivos.Contains(motivo) && Acepta.Contains("motivo")) d["motivo"] = motivo;
            d["duracion_s"] = Math.Max(0L, Math.Min(86400L, segundos));
            return new JavaScriptSerializer().Serialize(d);
        }

        static void Post(string mensaje, int espera)
        {
            byte[] cuerpo = Encoding.UTF8.GetBytes(mensaje);
            var req = (HttpWebRequest)WebRequest.Create(Direccion);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.UserAgent = "Instalar-Home";
            req.Timeout = espera;
            req.ReadWriteTimeout = espera;
            req.ContentLength = cuerpo.Length;
            using (var s = req.GetRequestStream()) s.Write(cuerpo, 0, cuerpo.Length);
            using (req.GetResponse()) { }
        }

        public static void Enviar(string mensaje, Estado e)
        {
            Trabajos.Iniciar(delegate
            {
                try
                {
                    Post(mensaje, 8000);
                    e.Anotar("Instalación contada en las estadísticas de Home.");
                }
                catch (WebException ex) { e.Anotar("No se ha podido contar en las estadísticas (no afecta a nada): " + Red.Explicar(ex)); }
                catch (IOException ex) { e.Anotar("No se ha podido contar en las estadísticas (no afecta a nada): " + Red.Explicar(ex)); }
            });
        }

        // Al cerrar la ventana: el programa espera como mucho unos segundos a que salga. Si no llega, no queda nadie a
        // quien contárselo: la ventana ya se ha cerrado
        public static void EnviarAlCerrar(string mensaje)
        {
            new Thread(() =>
            {
                try { Post(mensaje, 4000); }
                catch (WebException) { }
                catch (IOException) { }
            }) { IsBackground = false }.Start();
        }

        // ------------------------------------------------------------ informe de un fallo

        // Aparte de lo anterior: solo se envía si la persona lo pide, después de ver el texto exacto
        const string DireccionInforme = Rutas.Servidor + "/v1/informe";
        const int MaxRegistro = 11500;

        static string SinPalabra(string texto, string palabra, string cambio)
        {
            if (string.IsNullOrEmpty(palabra) || palabra.Trim().Length < 3) return texto;
            return Regex.Replace(texto, @"(?<![\w-])" + Regex.Escape(palabra.Trim()) + @"(?![\w-])", cambio, RegexOptions.IgnoreCase);
        }

        // El registro sin nada de la persona: ni el número de serie de la Askey, ni el usuario o el nombre del PC, ni
        // direcciones de correo, IP o MAC. Solo el final, que es donde está el fallo
        public static string Limpiar(string texto, params string[] series)
        {
            string t = Rutas.SinUsuario(texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (string s in series) t = SinPalabra(t, s, "askey");
            t = Regex.Replace(t, @"^[0-9A-Za-z]{6,24}(?=[ \t]+(fastboot|device|recovery|sideload|unauthorized)\b)", "askey", RegexOptions.Multiline);
            t = Regex.Replace(t, @"(serialno|serial)(\s*[:=]\s*)\S+", "$1$2askey", RegexOptions.IgnoreCase);
            t = SinPalabra(t, Environment.MachineName, "pc");
            t = SinPalabra(t, Environment.UserName, "usuario");
            t = Regex.Replace(t, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", "[correo]");
            t = Regex.Replace(t, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[ip]");
            t = Regex.Replace(t, @"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", "[mac]");
            t = Regex.Replace(t, @"[\x00-\x08\x0B-\x1F\x7F]", "");
            t = t.Trim();
            // Que quepa: como mucho MaxRegistro caracteres y, ya en JSON, menos de 28 KB (la recogida acepta algo más)
            int max = MaxRegistro;
            while (true)
            {
                string r = t.Length > max ? Final(t, max) : t;
                if (Encoding.UTF8.GetByteCount(new JavaScriptSerializer().Serialize(r)) <= 28000) return r;
                max = Math.Min(max, t.Length) * 3 / 4;
            }
        }

        // Los últimos caracteres de un texto, empezando en una línea entera y marcado como recortado
        static string Final(string t, int max)
        {
            t = t.Substring(t.Length - (max - 4));
            int n = t.IndexOf('\n');
            if (n >= 0 && n < 300) t = t.Substring(n + 1);
            return "[…]\n" + t;
        }

        public static string Informe(string home, string paso, string registro)
        {
            var d = new Dictionary<string, object>
            {
                { "v", 1 }, { "camino", "cable" }, { "instalador", Instalador }, { "home", home }, { "paso", paso }, { "registro", registro }
            };
            return new JavaScriptSerializer().Serialize(d);
        }

        // Lo envía y devuelve su código (H-7K3Q2M). Si no llega, null y la causa en «fallo». Se llama fuera del hilo de
        // la ventana
        public static string EnviarInforme(string mensaje, out string fallo)
        {
            fallo = "";
            try
            {
                byte[] cuerpo = Encoding.UTF8.GetBytes(mensaje);
                var req = (HttpWebRequest)WebRequest.Create(DireccionInforme);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "Instalar-Home";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.ContentLength = cuerpo.Length;
                using (var s = req.GetRequestStream()) s.Write(cuerpo, 0, cuerpo.Length);
                using (var r = req.GetResponse())
                using (var lector = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                {
                    var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(lector.ReadToEnd());
                    object c;
                    if (d != null && d.TryGetValue("codigo", out c) && c is string && Regex.IsMatch((string)c, "^H-[A-Z2-9]{6}$")) return (string)c;
                    fallo = "El servidor no ha devuelto un código.";
                }
            }
            catch (WebException ex) { fallo = Red.Explicar(ex); }
            catch (IOException ex) { fallo = Red.Explicar(ex); }
            catch (ArgumentException) { fallo = "El servidor ha contestado algo que no se entiende."; }
            catch (InvalidOperationException) { fallo = "El servidor ha contestado algo que no se entiende."; }
            return null;
        }
    }
}
