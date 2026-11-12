// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace HomeInstalador
{
    class PlanDescarga
    {
        public bool Herramientas, Home;
        public string Numero, Carpeta, ZipLocal, Url, Zip, Sums, Huella;
    }

    // Lo que se hace en segundo plano. Cada trabajo deja Fase en «hecho» o «fallo» y vacía Trabajo al acabar
    class Trabajos
    {
        // Únicas particiones en las que se puede escribir o que se pueden formatear
        static readonly string[] Permitidas = { "boot", "recovery", "vendor", "system", "splash", "cache", "userdata" };

        public const string Cancelado = "Se ha cancelado el permiso de administrador.";

        readonly Estado e;
        public Trabajos(Estado e) { this.e = e; }

        public static void Iniciar(ThreadStart t)
        {
            new Thread(t) { IsBackground = true }.Start();
        }

        Fastboot FB { get { return new Fastboot(e.FB); } }

        // Marca los pasos de un trabajo según avanza
        class Pasos
        {
            readonly List<Fase> l;
            int actual = -1;
            public Pasos(List<Fase> l) { this.l = l; }
            public void Ir(int i) { Terminar(); actual = i; if (l != null) l[i].Estado = "en_curso"; }
            public void Terminar() { if (actual >= 0 && l != null) l[actual].Estado = "hecho"; }
            public void Fallar() { if (actual >= 0 && l != null) l[actual].Estado = "fallo"; }
        }

        void Fallo(string m)
        {
            e.Error = m;
            e.Anotar("ERROR: " + m);
            e.Fase = "fallo";
        }

        // Lista de versiones del servidor de Home
        public void BuscarVersiones()
        {
            try
            {
                e.Releases = Red.Versiones();
                e.GitHub = "ok";
            }
            catch (Exception ex)
            {
                e.ErrorGitHub = ex.Message;
                e.GitHub = "sin_red";
            }
        }

        // Detecta el aparato cada 2 s
        public void Vigia()
        {
            string ultima = "";
            while (!e.Cerrar)
            {
                string t = e.Trabajo;
                if (t == "instalar" || t == "desbloquear") { ultima = ""; Thread.Sleep(500); continue; }
                if (e.Releer) { ultima = ""; e.Releer = false; }
                try
                {
                    if (string.IsNullOrEmpty(e.FB)) { e.Aparato = new Aparato("sin_fastboot"); Thread.Sleep(1000); continue; }
                    var fb = FB;
                    string[] lista = fb.Aparatos();
                    if (lista.Length == 0) { e.Aparato = new Aparato(EstadoUsb()); ultima = ""; }
                    else if (lista.Length > 1) { e.Aparato = new Aparato("varios"); ultima = ""; }
                    else if (lista[0] != ultima)
                    {
                        string v = fb.Variable("variant"), u = fb.Variable("unlocked");
                        string estado = v.IndexOf("Dragon eMMC", StringComparison.OrdinalIgnoreCase) < 0 ? "no_valido" : u != "yes" ? "bloqueada" : "ok";
                        e.Aparato = new Aparato(estado) { Serie = lista[0], Variante = v };
                        ultima = lista[0];
                    }
                }
                catch (Exception ex) { e.Aparato = new Aparato("error") { Texto = ex.Message }; ultima = ""; }
                Thread.Sleep(2000);
            }
        }

        // Sin nada en fastboot: ¿está conectada pero sin driver, o en modo EDL?
        static string EstadoUsb()
        {
            bool sinDriver = false, edl = false;
            try
            {
                const string consulta = "SELECT DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity " +
                    "WHERE DeviceID LIKE '%VID[_]18D1&PID[_]D00D%' OR DeviceID LIKE '%VID[_]05C6&PID[_]9008%'";
                using (var s = new ManagementObjectSearcher(consulta))
                using (var r = s.Get())
                    foreach (ManagementObject o in r)
                        using (o)
                        {
                            string id = Convert.ToString(o["DeviceID"]).ToUpperInvariant();
                            if (id.Contains("PID_9008")) edl = true;
                            else if (Convert.ToUInt32(o["ConfigManagerErrorCode"] ?? 0u) != 0) sinDriver = true;
                        }
            }
            catch (ManagementException) { }
            return edl ? "edl" : sinDriver ? "sin_driver" : "ninguno";
        }

        // Herramientas de Google y Home
        public void Descargar(PlanDescarga plan)
        {
            var pasos = new Pasos(e.Pasos);
            try
            {
                e.Progreso = -1;
                if (plan.Herramientas)
                {
                    pasos.Ir(0);
                    Directory.CreateDirectory(Rutas.Herramientas);
                    string zipH = Path.Combine(Rutas.Herramientas, "platform-tools-latest-windows.zip");
                    e.Contar("Descargando las herramientas de Google (fastboot)…");
                    Red.Bajar(Rutas.UrlHerramientas, zipH, e);
                    e.Progreso = -1;
                    string vieja = Path.Combine(Rutas.Herramientas, "platform-tools");
                    if (Directory.Exists(vieja)) Directory.Delete(vieja, true);
                    Imagenes.Descomprimir(zipH, Rutas.Herramientas, null);
                    File.Delete(zipH);
                    string fb = Path.Combine(vieja, "fastboot.exe");
                    if (!File.Exists(fb)) throw new Exception("El paquete de Google no trae fastboot.exe.");
                    e.FB = fb;
                    e.Anotar("Herramientas listas: " + fb);
                }
                if (plan.Home)
                {
                    Directory.CreateDirectory(plan.Carpeta);
                    string zip = plan.ZipLocal ?? Path.Combine(plan.Carpeta, plan.Zip);
                    string nombreZip = Path.GetFileName(zip);
                    string esperado = null;
                    if (!string.IsNullOrEmpty(plan.Sums))
                    {
                        e.Contar("Leyendo SHA256SUMS de la release…");
                        string patron = @"\s\*?" + Regex.Escape(nombreZip) + @"\s*$";
                        string linea = Red.Texto(plan.Sums).Split('\n').FirstOrDefault(l => Regex.IsMatch(l, patron));
                        if (linea != null) esperado = linea.Trim().Split(' ', '\t')[0].ToLowerInvariant();
                    }
                    // Sin SHA256SUMS, la huella que GitHub guarda de cada archivo de la release (viene en la lista)
                    if (esperado == null && plan.Huella != null && Regex.IsMatch(plan.Huella, "^[0-9a-f]{64}$"))
                    {
                        esperado = plan.Huella;
                        e.Anotar("Huella SHA256 tomada de la lista de versiones.");
                    }

                    pasos.Ir(1);
                    if (plan.ZipLocal == null)
                    {
                        if (File.Exists(zip) && esperado != null && Imagenes.Sha(zip) == esperado) e.Anotar("El zip ya estaba descargado.");
                        else { e.Contar("Descargando " + nombreZip + "…"); e.Progreso = 0; Red.Bajar(plan.Url, zip, e); }
                    }

                    pasos.Ir(2);
                    e.Progreso = -1;
                    if (esperado != null)
                    {
                        e.Contar("Comprobando la huella SHA256 del zip…");
                        if (Imagenes.Sha(zip) != esperado)
                        {
                            if (plan.ZipLocal == null) File.Delete(zip);
                            throw new Exception("La huella del zip no coincide con SHA256SUMS: está dañado." +
                                (plan.ZipLocal == null ? " Se ha borrado; vuelve a descargarlo." : ""));
                        }
                        e.Anotar("SHA256 del zip correcta.");
                    }
                    else e.Anotar("Sin SHA256SUMS: se comprueba cada imagen con su .sha256.");

                    pasos.Ir(3);
                    e.Contar("Descomprimiendo…");
                    e.Progreso = 0;
                    Imagenes.Descomprimir(zip, plan.Carpeta, p => e.Progreso = p);
                    e.Progreso = -1;

                    pasos.Ir(4);
                    string carpetaV = Path.Combine(plan.Carpeta, Path.GetFileNameWithoutExtension(nombreZip));
                    foreach (string p in Imagenes.Particiones)
                    {
                        e.Contar("Comprobando " + p + ".img…");
                        Imagenes.Comprobar(carpetaV, p);
                    }
                    if (plan.ZipLocal == null) { File.Delete(zip); e.Anotar("Zip borrado: las imágenes ya están comprobadas."); }
                    e.Anotar("Home " + plan.Numero + " listo en " + carpetaV);
                }
                pasos.Terminar();
                e.Fase = "hecho";
            }
            catch (Exception ex) { pasos.Fallar(); Fallo(ex.Message); }
            finally { e.Trabajo = ""; e.Cancelar = false; }
        }

        // Google USB Driver
        public void Driver()
        {
            try
            {
                string carpeta = Path.Combine(Rutas.Herramientas, "usb_driver");
                string inf = Path.Combine(carpeta, "android_winusb.inf");
                if (!File.Exists(inf))
                {
                    Directory.CreateDirectory(Rutas.Herramientas);
                    string zip = Path.Combine(Rutas.Herramientas, "usb_driver.zip");
                    e.Contar("Descargando el Google USB Driver…");
                    Red.Bajar(Rutas.UrlDriver, zip, e);
                    Imagenes.Descomprimir(zip, Rutas.Herramientas, null);
                    File.Delete(zip);
                    if (!File.Exists(inf)) throw new Exception("El paquete de Google no trae android_winusb.inf.");
                }
                e.Contar("Instalando el driver (Windows pide permiso de administrador)…");
                e.Progreso = -1;
                // Un proceso de 32 bits en Windows de 64 no ve el pnputil de System32
                string sistema = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative")
                    : Environment.SystemDirectory;
                var psi = new ProcessStartInfo(Path.Combine(sistema, "pnputil.exe"), "/add-driver \"" + inf + "\" /install")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                int codigo;
                using (var p = Process.Start(psi)) { p.WaitForExit(); codigo = p.ExitCode; }
                if (codigo != 0 && codigo != 259 && codigo != 3010) throw new Exception("pnputil terminó con el código " + codigo + ".");
                e.Anotar("Driver instalado (" + inf + ").");
                e.Fase = "hecho";
            }
            catch (Exception ex)
            {
                var w = ex as Win32Exception;
                Fallo(w != null && w.NativeErrorCode == 1223 ? Cancelado : ex.Message);
            }
            finally { e.Trabajo = ""; e.Releer = true; }
        }

        public void Desbloquear()
        {
            try
            {
                var fb = FB;
                e.Contar("fastboot flashing unlock (mira la pantalla de la Askey)…");
                fb.Lanzar(new[] { "flashing", "unlock" }, l => e.Anotar("  " + l));
                Thread.Sleep(2000);
                string u = fb.Variable("unlocked");
                if (u != "yes")
                {
                    e.Contar("fastboot oem unlock (mira la pantalla de la Askey)…");
                    fb.Lanzar(new[] { "oem", "unlock" }, l => e.Anotar("  " + l));
                    Thread.Sleep(2000);
                    u = fb.Variable("unlocked");
                }
                if (u != "yes")
                    throw new Exception("Sigue bloqueado. Si en la Askey sale una pregunta, acéptala con los botones de volumen y encendido, y vuelve a pulsar Desbloquear.");
                e.Anotar("Bootloader desbloqueado.");
                e.Fase = "hecho";
            }
            catch (Exception ex) { Fallo(ex.Message); }
            finally { e.Trabajo = ""; e.Releer = true; }
        }

        // Única salida hacia fastboot que escribe: revisa cada orden antes de lanzarla
        void Ejecutar(Fastboot fb, string[] a, Action<string> cadaLinea)
        {
            if ((a[0] != "flash" && a[0] != "format") || Array.IndexOf(Permitidas, a[1]) < 0)
                throw new Exception("NEGADO: fastboot " + string.Join(" ", a));
            e.Anotar("> fastboot " + string.Join(" ", a));
            var r = fb.Lanzar(a, l => { e.Anotar("  " + l); if (cadaLinea != null) cadaLinea(l); });
            if (r.Codigo != 0) throw new Exception(string.Format("fastboot falló ({0}) en: fastboot {1} {2}", r.Codigo, a[0], a[1]));
        }

        public void Instalar(string carpeta, string serie)
        {
            var pasos = new Pasos(e.Pasos);
            try
            {
                var fb = FB;

                // 1) Comprobar las imágenes
                pasos.Ir(0);
                e.Progreso = -1;
                var pesos = new Dictionary<string, long>();
                foreach (string p in Imagenes.Particiones)
                {
                    e.Contar("Comprobando " + p + ".img…");
                    Imagenes.Comprobar(carpeta, p);
                    pesos[p] = new FileInfo(Path.Combine(carpeta, p + ".img")).Length;
                }
                e.Anotar("Imágenes comprobadas.");

                // 2) El aparato sigue ahí, es el mismo y sigue desbloqueado
                string[] lista = fb.Aparatos();
                if (lista.Length != 1 || lista[0] != serie) throw new Exception("La Askey ya no está en fastboot (o hay otro aparato). No se ha grabado nada.");
                if (fb.Variable("unlocked") != "yes") throw new Exception("El bootloader está bloqueado. No se ha grabado nada.");

                // 3) Grabar, repartiendo la barra según el tamaño de cada imagen
                const long formato = 64L << 20;
                double total = pesos.Values.Sum() + 2 * formato, hecho = 0;
                int i = 1;
                foreach (string p in Imagenes.Particiones)
                {
                    pasos.Ir(i++);
                    e.Contar("Grabando " + p + "…");
                    long peso = pesos[p];
                    double base0 = hecho;
                    Ejecutar(fb, new[] { "flash", p, Path.Combine(carpeta, p + ".img") }, l =>
                    {
                        double? f = null;
                        Match m;
                        if ((m = Regex.Match(l, @"Sending sparse '[^']+' (\d+)/(\d+)")).Success) f = (int.Parse(m.Groups[1].Value) - 1.0) / int.Parse(m.Groups[2].Value);
                        else if ((m = Regex.Match(l, @"Writing sparse '[^']+' (\d+)/(\d+)")).Success) f = (int.Parse(m.Groups[1].Value) - 0.5) / int.Parse(m.Groups[2].Value);
                        else if (l.StartsWith("Sending '")) f = 0.1;
                        else if (l.StartsWith("Writing '")) f = 0.6;
                        if (f.HasValue) e.Progreso = 100 * (base0 + f.Value * peso) / total;
                    });
                    hecho += peso;
                    e.Progreso = 100 * hecho / total;
                }

                // 4) Formatear cache y después userdata
                foreach (string p in new[] { "cache", "userdata" })
                {
                    pasos.Ir(i++);
                    e.Contar("Formateando " + p + "…");
                    Ejecutar(fb, new[] { "format", p }, null);
                    hecho += formato;
                    e.Progreso = 100 * hecho / total;
                }
                pasos.Terminar();
                e.Progreso = 100;
                e.Contar("Hecho.");
                e.Fase = "hecho";
            }
            catch (Exception ex) { pasos.Fallar(); Fallo(ex.Message); }
            finally { e.Trabajo = ""; }
        }
    }
}
