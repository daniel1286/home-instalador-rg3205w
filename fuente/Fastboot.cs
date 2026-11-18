// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HomeInstalador
{
    // fastboot de las herramientas de Google que baja el instalador. Nunca se usa otro: uno antiguo del PATH puede no
    // saber enviar imágenes por partes ni desbloquear
    class Fastboot
    {
        public class Resultado
        {
            public int Codigo;
            public string Salida, Error;
        }

        // Cuánto puede tardar una orden. Total: tope fijo. Espera: cuánto se espera a que vuelva la Askey si fastboot dice
        // «waiting for». Silencio: sin salida durante ese tiempo se mira si la Askey sigue conectada
        public class Limites
        {
            public TimeSpan Total = TimeSpan.Zero, Espera = TimeSpan.FromSeconds(8), Silencio = TimeSpan.FromSeconds(20);
            public TimeSpan MaxSilencio = TimeSpan.FromMinutes(10);
            public Func<bool> Cancelar;
            public string SiTiempo = "No contesta";
        }

        public static readonly Limites Corta = new Limites { Total = TimeSpan.FromSeconds(15) };

        public static readonly string Carpeta = Path.Combine(Rutas.Herramientas, "platform-tools");
        public static readonly string Propio = Path.Combine(Carpeta, "fastboot.exe");
        static readonly string Huellas = Path.Combine(Rutas.Herramientas, "platform-tools.sha256");
        static readonly string[] Archivos = { "fastboot.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll" };
        // A partir de esta versión fastboot envía por partes las imágenes grandes y conoce «flashing unlock»
        const int VersionMinima = 31;

        readonly string ruta, serie;
        public Fastboot(string ruta, string serie = null) { this.ruta = ruta; this.serie = string.IsNullOrEmpty(serie) ? null : serie; }

        // ok | falta | danado | antiguo. Al bajarlas se apunta la huella de cada archivo; si cambia, alguien las ha tocado
        public static string Revisar()
        {
            foreach (string a in Archivos) if (!File.Exists(Path.Combine(Carpeta, a))) return "falta";
            try
            {
                if (!File.Exists(Huellas)) Apuntar();
                var apuntadas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string l in File.ReadAllLines(Huellas))
                {
                    var m = Regex.Match(l, @"^([0-9a-f]{64})\s+\*?(\S+)$");
                    if (m.Success) apuntadas[m.Groups[2].Value] = m.Groups[1].Value;
                }
                foreach (string a in Archivos)
                {
                    string h;
                    if (!apuntadas.TryGetValue(a, out h) || Imagenes.Sha(Path.Combine(Carpeta, a)) != h) return "danado";
                }
                var r = new Fastboot(Propio).Lanzar(new[] { "--version" }, null, Corta);
                var v = Regex.Match(r.Salida + r.Error, @"fastboot version (\d+)\.");
                if (r.Codigo != 0 || !v.Success) return "danado";
                return int.Parse(v.Groups[1].Value) < VersionMinima ? "antiguo" : "ok";
            }
            catch (Bloqueado) { return "quitado"; }
            catch (IOException) { return "danado"; }
            catch (UnauthorizedAccessException) { return "quitado"; }
            catch (Fallo) { return "danado"; }
        }

        public static void Apuntar()
        {
            var sb = new StringBuilder();
            foreach (string a in Archivos) sb.Append(Imagenes.Sha(Path.Combine(Carpeta, a))).Append("  ").Append(a).Append("\r\n");
            File.WriteAllText(Huellas, sb.ToString());
        }

        // El antivirus ha quitado o bloqueado fastboot.exe (o una de sus DLL)
        public class Bloqueado : Exception
        {
            public Bloqueado(Exception causa) : base("El antivirus ha quitado o bloqueado fastboot.exe.", causa) { }
        }

        // Procesos de fastboot en marcha, para pararlos si se cierra la ventana y no confundirlos con los de otros programas
        static readonly HashSet<Process> enMarcha = new HashSet<Process>();

        public static int[] Propios()
        {
            lock (enMarcha)
            {
                var l = new List<int>();
                foreach (var p in enMarcha) try { l.Add(p.Id); } catch (InvalidOperationException) { }
                return l.ToArray();
            }
        }

        public static void PararTodos()
        {
            lock (enMarcha)
                foreach (var p in enMarcha)
                    try { if (!p.HasExited) p.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
        }

        // Un argumento tal como lo lee CommandLineToArgvW: comillas si hace falta y barras antes de comillas duplicadas
        public static string Argumento(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int barras = 0;
            foreach (char c in a)
            {
                if (c == '\\') { barras++; continue; }
                sb.Append('\\', c == '"' ? barras * 2 + 1 : barras);
                barras = 0;
                sb.Append(c);
            }
            sb.Append('\\', barras * 2);
            return sb.Append('"').ToString();
        }

        class Lectura
        {
            public long Ultima, Esperando = -1;
        }

        // Lanza fastboot sin ventana; cadaLinea recibe cada línea de stderr (donde fastboot cuenta lo que hace). Si la
        // Askey se va, no contesta o se cancela, para el proceso y lanza un Fallo (o una Cancelacion)
        public Resultado Lanzar(string[] args, Action<string> cadaLinea, Limites lim)
        {
            var todos = serie != null ? new[] { "-s", serie }.Concat(args) : args;
            var psi = new ProcessStartInfo(ruta, string.Join(" ", todos.Select(Argumento)))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(ruta) ?? ""
            };
            Process p;
            try { p = Process.Start(psi); }
            catch (Win32Exception w)
            {
                // 2/3: no está; 5: acceso denegado; 225: el antivirus lo ha bloqueado; 193/216: archivo dañado
                if (new[] { 2, 3, 5, 193, 216, 225, 1260 }.Contains(w.NativeErrorCode)) throw new Bloqueado(w);
                throw;
            }
            lock (enMarcha) enMarcha.Add(p);
            var reloj = Stopwatch.StartNew();
            var lec = new Lectura();
            var err = new StringBuilder();
            var sal = new StringBuilder();
            try
            {
                p.ErrorDataReceived += (s, a) =>
                {
                    if (a.Data == null) return;
                    lock (err) err.AppendLine(a.Data);
                    Interlocked.Exchange(ref lec.Ultima, reloj.ElapsedMilliseconds);
                    if (a.Data.Contains("< waiting for")) Interlocked.CompareExchange(ref lec.Esperando, reloj.ElapsedMilliseconds, -1);
                    if (cadaLinea != null) cadaLinea(a.Data);
                };
                p.OutputDataReceived += (s, a) => { if (a.Data != null) lock (sal) sal.AppendLine(a.Data); };
                p.BeginErrorReadLine();
                p.BeginOutputReadLine();

                string motivo = null, texto = null;
                long revisado = 0;
                int ausente = 0;
                while (!p.WaitForExit(250))
                {
                    long ahora = reloj.ElapsedMilliseconds, esperando = Interlocked.Read(ref lec.Esperando);
                    double callado = (ahora - Interlocked.Read(ref lec.Ultima)) / 1000.0;
                    if (lim.Cancelar != null && lim.Cancelar()) { motivo = "cancelado"; break; }
                    if (lim.Total > TimeSpan.Zero && reloj.Elapsed > lim.Total) { motivo = "tiempo"; texto = lim.SiTiempo; break; }
                    if (esperando >= 0 && ahora - esperando > lim.Espera.TotalMilliseconds) { motivo = "desconectada"; break; }
                    if (callado > lim.MaxSilencio.TotalSeconds) { motivo = "no_responde"; break; }
                    // Callado un rato: puede estar escribiendo una imagen grande, o puede que la Askey ya no esté
                    if (callado > lim.Silencio.TotalSeconds && ahora - revisado > 5000)
                    {
                        revisado = ahora;
                        ausente = Usb.AskeyPresente() ? 0 : ausente + 1;
                        if (ausente >= 2) { motivo = "desconectada"; break; }
                    }
                }
                if (motivo != null)
                {
                    try { p.Kill(); } catch (InvalidOperationException) { } catch (Win32Exception) { }
                    p.WaitForExit(3000);
                    if (motivo == "cancelado") throw new Cancelacion("Cancelado.");
                    if (motivo == "desconectada") throw new Fallo("desconectada", "Se ha desconectado la Askey.");
                    if (motivo == "no_responde") throw new Fallo("no_responde", "La Askey ha dejado de contestar.");
                    throw new Fallo("tiempo", texto);
                }
                p.WaitForExit();
                string e, o;
                lock (err) e = err.ToString();
                lock (sal) o = sal.ToString();
                return new Resultado { Codigo = p.ExitCode, Salida = o, Error = e };
            }
            finally
            {
                lock (enMarcha) enMarcha.Remove(p);
                p.Dispose();
            }
        }

        public string Variable(string nombre, Limites lim = null)
        {
            var r = Lanzar(new[] { "getvar", nombre }, null, lim ?? Corta);
            foreach (string l in r.Error.Split('\n'))
                if (l.StartsWith(nombre + ":")) return l.Substring(nombre.Length + 1).Trim();
            return "";
        }

        public string[] Aparatos()
        {
            var lista = new List<string>();
            foreach (string l in Lanzar(new[] { "devices" }, null, Corta).Salida.Split('\n'))
            {
                string[] c = l.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (c.Length > 0) lista.Add(c[0]);
            }
            return lista.ToArray();
        }

        // Lo que fastboot ha dicho al fallar, contado para la persona. Solo mira las líneas de error: «Invalid sparse
        // file format at header magic» sale siempre que se envía system y no es un fallo
        public static Fallo Explicar(string orden, string salida)
        {
            var errores = (salida ?? "").Split('\n').Select(l => l.Trim())
                .Where(l => Regex.IsMatch(l, @"FAILED|error|remote:", RegexOptions.IgnoreCase)).ToList();
            string t = string.Join("\n", errores);
            Func<string, bool> hay = patron => Regex.IsMatch(t, patron, RegexOptions.IgnoreCase);
            if (hay(@"not allowed|(?<!un)locked|unlock"))
                return new Fallo("bloqueada", "La Askey no deja grabar: el bootloader está bloqueado. Vuelve a desbloquearlo y pulsa Volver a intentarlo.");
            if (hay(@"too large|exceeds|size too"))
                return new Fallo("tamano", "Una imagen no cabe en su partición. Este paquete no es para este aparato: descarga Home desde la lista de versiones.");
            if (hay(@"busy|claim|in use"))
                return new Fallo("ocupado", "Otro programa está usando la Askey (otro fastboot, adb, Android Studio o un programa para móviles). Ciérralo y pulsa Volver a intentarlo.");
            if (hay(@"write to device|read from device|no link|status read|data transfer|transport|unknown error|gen_failure|timed out|timeout"))
                return new Fallo("usb", "Se ha cortado la comunicación por USB. Conecta la Askey a otro puerto (mejor USB 2.0 y sin hub) o prueba otro cable de datos, vuelve a ponerla en fastboot y pulsa Volver a intentarlo.");
            if (hay(@"sparse|cannot load|failed to load|unknown option|invalid option"))
                return new Fallo("herramientas", "Las herramientas de Google están dañadas. Vuelve a bajarlas en «Opciones» y pulsa Volver a intentarlo.");
            var remoto = Regex.Match(t, @"remote:\s*'([^']+)'");
            if (remoto.Success)
                return new Fallo("aparato", "La Askey ha rechazado «" + orden + "»: «" + remoto.Groups[1].Value + "». Mira «Ver detalles» y, si no sabes qué hacer, envía el informe del fallo.");
            return new Fallo("fastboot", "fastboot no ha podido terminar «" + orden + "». Mira «Ver detalles» y, si no sabes qué hacer, envía el informe del fallo.");
        }
    }
}
