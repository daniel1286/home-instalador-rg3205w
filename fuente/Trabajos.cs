// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace HomeInstalador
{
    class PlanDescarga
    {
        public bool Herramientas, Home;
        public string Numero, Carpeta, ZipLocal, Url, Zip, Sums, Huella;
        public long Tamano;
    }

    // Lo que se hace en segundo plano. Cada trabajo deja Fase en «hecho» o «fallo» y vacía Trabajo al acabar
    class Trabajos
    {
        public const string Cancelado = "Se ha cancelado el permiso de administrador.";
        public const string DesbloqueoCancelado = "Desbloqueo cancelado.";
        public const string InstalacionDetenida = "Instalación detenida. La Askey no tiene un sistema completo: vuelve a instalar Home antes de reiniciarla.";

        readonly Estado e;
        public Trabajos(Estado e) { this.e = e; }

        // Un hilo de fondo. Un fallo sin controlar no cierra el programa: se apunta y se enseña
        public static void Iniciar(ThreadStart t)
        {
            new Thread(() =>
            {
                try { t(); }
                catch (Exception ex) { Programa.Fallo(ex); }
            }) { IsBackground = true }.Start();
        }

        bool Parar() { return e.Cancelar || e.Cerrar; }

        // Marca los pasos de un trabajo según avanza
        class Pasos
        {
            readonly List<Fase> l;
            int actual = -1;
            public Pasos(List<Fase> l) { this.l = l; }
            public void Ir(string nombre)
            {
                Terminar();
                actual = l == null ? -1 : l.FindIndex(f => f.Nombre == nombre);
                if (actual >= 0) l[actual].Estado = "en_curso";
            }
            public void Terminar() { if (actual >= 0) l[actual].Estado = "hecho"; }
            public void Fallar() { if (actual >= 0) l[actual].Estado = "fallo"; }
        }

        void Terminar(Exception ex, Pasos pasos)
        {
            if (ex is Cancelacion)
            {
                e.Motivo = "cancelado";
                e.Error = ex.Message;
                e.Anotar(ex.Message);
                e.Fase = "fallo";
                return;
            }
            if (pasos != null) pasos.Fallar();
            var f = ex as Fallo;
            if (f != null) e.Motivo = f.Motivo;
            else if (Imagenes.DiscoLleno(ex)) { e.Motivo = "espacio"; ex = Imagenes.FaltaEspacio(Rutas.Datos, 0, 0); }
            else if (ex is Fastboot.Bloqueado) { e.Motivo = "herramientas"; e.FB = null; e.Herr = "quitado"; }
            else e.Motivo = "otro";
            e.Error = ex is Fastboot.Bloqueado
                ? "El antivirus ha quitado o bloqueado fastboot.exe. Vuelve a bajar las herramientas y, si pasa otra vez, añade " + Rutas.SinUsuario(Fastboot.Carpeta) + " a las exclusiones del antivirus."
                : ex.Message;
            e.Anotar("ERROR: " + e.Error + (f == null && !(ex is Fastboot.Bloqueado) ? " (" + ex.GetType().Name + ")" : ""));
            e.Fase = "fallo";
        }

        // Lista de versiones del servidor de Home (o de GitHub si el servidor no contesta)
        public void BuscarVersiones()
        {
            try
            {
                string fuente, fallo;
                e.Releases = Red.Versiones(out fuente, out fallo);
                e.FuenteVersiones = fuente;
                if (fallo != "") e.Anotar("El servidor de Home no contesta (" + fallo + "): las versiones salen directamente de GitHub.");
                e.ErrorGitHub = "";
                e.GitHub = "ok";
            }
            catch (Exception ex)
            {
                e.ErrorGitHub = ex is Fallo || ex is FormatException ? ex.Message : Red.Explicar(ex);
                e.Anotar("Sin lista de versiones: " + e.ErrorGitHub);
                e.GitHub = "sin_red";
            }
        }

        // Comprueba fastboot al empezar y detecta el aparato cada 2 s
        public void Vigia()
        {
            Usb.Empezar();
            string ultima = "";
            while (!e.Cerrar)
            {
                string t = e.Trabajo;
                if (t == "instalar" || t == "desbloquear" || t == "driver") { ultima = ""; Thread.Sleep(500); continue; }
                if (e.Releer) { ultima = ""; e.Releer = false; }
                try
                {
                    // El antivirus puede quitar fastboot en cualquier momento: se mira en cada vuelta
                    if (string.IsNullOrEmpty(e.FB) || !File.Exists(e.FB))
                    {
                        if (!string.IsNullOrEmpty(e.FB)) { e.FB = null; e.Herr = "quitado"; e.Anotar("fastboot.exe ya no está en " + Rutas.SinUsuario(Fastboot.Carpeta) + "."); }
                        e.Aparato = new Aparato("sin_fastboot");
                        Thread.Sleep(1000);
                        continue;
                    }
                    var fb = new Fastboot(e.FB);
                    string[] lista = fb.Aparatos();
                    if (lista.Length == 0) { e.Aparato = new Aparato(Usb.Estado()); ultima = ""; }
                    else if (lista.Length > 1) { e.Aparato = new Aparato("varios"); ultima = ""; }
                    else if (lista[0] != ultima)
                    {
                        var esta = new Fastboot(e.FB, lista[0]);
                        string v = esta.Variable("variant"), u = esta.Variable("unlocked");
                        string estado = v.IndexOf("Dragon eMMC", StringComparison.OrdinalIgnoreCase) < 0 ? "no_valido" : u != "yes" ? "bloqueada" : "ok";
                        e.Aparato = new Aparato(estado) { Serie = lista[0], Variante = v };
                        ultima = lista[0];
                    }
                }
                catch (Fastboot.Bloqueado)
                {
                    e.FB = null; e.Herr = "quitado";
                    e.Anotar("Windows no deja abrir fastboot.exe: el antivirus lo ha quitado o bloqueado.");
                    e.Aparato = new Aparato("sin_fastboot");
                }
                // Se ha desconectado entre una orden y la siguiente: en la próxima vuelta se ve qué hay
                catch (Fallo) { e.Aparato = new Aparato(Usb.Estado()); ultima = ""; }
                catch (Exception ex) { e.Aparato = new Aparato("error") { Texto = ex.Message }; ultima = ""; }
                Thread.Sleep(2000);
            }
        }

        // ------------------------------------------------------------ descargar

        string BuscarHuella(PlanDescarga plan, string nombreZip)
        {
            string esperado = null;
            if (!string.IsNullOrEmpty(plan.Sums))
            {
                e.Contar("Leyendo las huellas de la versión…");
                try
                {
                    string patron = @"\s\*?" + Regex.Escape(nombreZip) + @"\s*$";
                    string linea = Red.Texto(plan.Sums).Split('\n').FirstOrDefault(l => Regex.IsMatch(l, patron));
                    if (linea != null) esperado = linea.Trim().Split(' ', '\t')[0].ToLowerInvariant();
                }
                catch (System.Net.WebException ex) { e.Anotar("  No se ha podido leer SHA256SUMS: " + Red.Explicar(ex)); }
            }
            // Sin SHA256SUMS, la huella que GitHub guarda de cada archivo de la release (viene en la lista)
            if (esperado == null && plan.Huella != null && Regex.IsMatch(plan.Huella, "^[0-9a-f]{64}$"))
            {
                esperado = plan.Huella;
                e.Anotar("Huella SHA256 tomada de la lista de versiones.");
            }
            return esperado;
        }

        string ShaConProgreso(string archivo, string texto)
        {
            e.Contar(texto);
            e.Progreso = 0;
            string h = Imagenes.Sha(archivo, Parar, p => e.Progreso = p);
            e.Progreso = -1;
            return h;
        }

        // Herramientas de Google y Home
        public void Descargar(PlanDescarga plan)
        {
            var pasos = new Pasos(e.Pasos);
            try
            {
                e.Progreso = -1;
                if (plan.Herramientas) BajarHerramientas(pasos);
                if (plan.Home)
                {
                    Directory.CreateDirectory(plan.Carpeta);
                    string zip = plan.ZipLocal ?? Path.Combine(plan.Carpeta, plan.Zip);
                    string nombreZip = plan.ZipLocal != null ? Path.GetFileName(plan.Zip ?? zip) : plan.Zip;
                    string esperado = BuscarHuella(plan, nombreZip);

                    pasos.Ir("descargar");
                    if (plan.ZipLocal == null)
                    {
                        if (File.Exists(zip) && esperado != null && ShaConProgreso(zip, "Mirando el zip que ya estaba…") == esperado) e.Anotar("El zip ya estaba descargado.");
                        else
                        {
                            // El zip y lo que sale de él: unas tres veces el zip
                            Imagenes.HaceFalta(plan.Carpeta, plan.Tamano * 3);
                            e.Contar("Descargando " + nombreZip + "…"); e.Progreso = 0; Red.Bajar(plan.Url, zip, e);
                        }
                    }

                    pasos.Ir("zip");
                    e.Progreso = -1;
                    if (esperado != null)
                    {
                        if (ShaConProgreso(zip, "Comprobando la huella del zip…") != esperado)
                        {
                            if (plan.ZipLocal != null) throw new Fallo("danado", "Este zip está dañado: su huella no coincide con la de la versión publicada. Vuelve a descargarlo.");
                            // Una segunda descarga entera antes de dar el fallo
                            File.Delete(zip);
                            e.Anotar("La huella del zip no coincide: se descarga otra vez.");
                            pasos.Ir("descargar");
                            e.Contar("Descargando otra vez " + nombreZip + "…"); e.Progreso = 0; Red.Bajar(plan.Url, zip, e);
                            pasos.Ir("zip");
                            if (ShaConProgreso(zip, "Comprobando la huella del zip…") != esperado)
                            {
                                File.Delete(zip);
                                throw new Fallo("danado", "El archivo ha llegado dañado dos veces. Revisa la conexión (o prueba otra red) y vuelve a pulsar Descargar.");
                            }
                        }
                        e.Anotar("SHA256 del zip correcta.");
                    }
                    else e.Anotar("Sin huella del zip: se comprueba cada imagen con su .sha256.");

                    pasos.Ir("descomprimir");
                    string raiz = Imagenes.RaizDelZip(zip);
                    if (raiz == null) throw new Fallo("paquete", "Este zip no es un paquete de Home para el RG3205W.");
                    Imagenes.HaceFalta(plan.Carpeta, Imagenes.Descomprimido(zip) + (64L << 20));
                    e.Contar("Descomprimiendo…");
                    e.Progreso = 0;
                    Imagenes.Descomprimir(zip, plan.Carpeta, p => e.Progreso = p, Parar);
                    e.Progreso = -1;

                    pasos.Ir("imagenes");
                    string carpetaV = Path.Combine(plan.Carpeta, raiz);
                    foreach (var p in Imagenes.DelPaquete(carpetaV))
                    {
                        e.Contar("Comprobando " + p.Key + ".img…");
                        Imagenes.Comprobar(carpetaV, p.Key, p.Value, Parar);
                    }
                    if (plan.ZipLocal == null) { File.Delete(zip); e.Anotar("Zip borrado: las imágenes ya están comprobadas."); }
                    e.Anotar("Home " + plan.Numero + " listo en " + carpetaV);
                }
                pasos.Terminar();
                e.Fase = "hecho";
            }
            catch (Cancelacion) { Terminar(new Cancelacion(Red.Detenida), pasos); }
            catch (Exception ex) { Terminar(ex, pasos); }
            finally { e.Trabajo = ""; e.Cancelar = false; }
        }

        void BajarHerramientas(Pasos pasos)
        {
            pasos.Ir("herramientas");
            Directory.CreateDirectory(Rutas.Herramientas);
            string zipH = Path.Combine(Rutas.Herramientas, "platform-tools-latest-windows.zip");
            e.Contar("Descargando las herramientas de Google (fastboot)…");
            Red.Bajar(Rutas.UrlHerramientas, zipH, e);
            e.Progreso = -1;
            // Que el vigía deje de usar el fastboot viejo antes de borrarlo
            e.FB = null;
            for (int i = 0; i < 50 && Fastboot.Propios().Length > 0; i++) Thread.Sleep(100);
            if (Directory.Exists(Fastboot.Carpeta)) Directory.Delete(Fastboot.Carpeta, true);
            Imagenes.Descomprimir(zipH, Rutas.Herramientas, null, Parar);
            File.Delete(zipH);
            if (!File.Exists(Fastboot.Propio)) throw new Fallo("herramientas", "El paquete de Google no trae fastboot.exe.");
            Fastboot.Apuntar();
            string r = Fastboot.Revisar();
            if (r == "quitado") throw new Fastboot.Bloqueado(null);
            if (r != "ok") throw new Fallo("herramientas", "Las herramientas de Google recién bajadas no funcionan (" + r + "). Vuelve a intentarlo.");
            e.FB = Fastboot.Propio;
            e.Herr = "ok";
            e.Anotar("Herramientas listas: " + Rutas.SinUsuario(Fastboot.Propio));
        }

        // ------------------------------------------------------------ driver

        // Google USB Driver: se baja si hace falta y se asigna a la Askey con permiso de administrador. Solo se da por
        // hecho si después Windows dice que la Askey funciona con él
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
                    e.Contar("Descargando el driver USB de Google…");
                    Red.Bajar(Rutas.UrlDriver, zip, e);
                    Imagenes.Descomprimir(zip, Rutas.Herramientas, null, Parar);
                    File.Delete(zip);
                    if (!File.Exists(inf)) throw new Fallo("driver", "El paquete de Google no trae android_winusb.inf.");
                }
                e.Contar("Asignando el driver a la Askey (Windows pide permiso de administrador)…");
                e.Progreso = -1;
                string detalle;
                int r = DriverUsb.Asignar(inf, out detalle);
                if (detalle.Length > 0) e.Anotar("  " + detalle);
                if (r == DriverUsb.SinAparato)
                    throw new Fallo("conectar", "La Askey no está conectada en modo fastboot. Conéctala y vuelve a pulsar Instalar el driver.");
                if (r == DriverUsb.SinDriver)
                    throw new Fallo("driver", "El driver de Google no trae la interfaz de fastboot. Asígnalo a mano con el capítulo 3 de la guía.");
                if (r != DriverUsb.Ok && r != DriverUsb.OkReiniciar)
                    throw new Fallo("driver", "Windows no ha dejado asignar el driver. Asígnalo a mano con el capítulo 3 de la guía.");
                // Windows tarda un momento en volver a arrancar el aparato con el driver nuevo
                uint? codigo = null;
                for (int i = 0; i < 20; i++)
                {
                    codigo = Usb.CodigoAskey();
                    if (codigo == 0) break;
                    Thread.Sleep(500);
                }
                if (codigo == null) throw new Fallo("conectar", "Se ha desconectado la Askey mientras se asignaba el driver. Vuelve a conectarla en fastboot.");
                if (codigo != 0)
                    throw new Fallo("driver", r == DriverUsb.OkReiniciar
                        ? "Windows pide reiniciar el PC para terminar de instalar el driver. Reinícialo y vuelve a abrir el instalador."
                        : "Windows ha aceptado el driver, pero la Askey sigue sin funcionar con él (código " + codigo + "). Desconecta y vuelve a conectar el USB; si sigue igual, asígnalo a mano con el capítulo 3 de la guía.");
                e.Anotar("Driver asignado: la Askey ya funciona con el driver de Google.");
                e.Fase = "hecho";
            }
            catch (Win32Exception w)
            {
                if (w.NativeErrorCode != 1223) { Terminar(w, null); return; }
                if (Sistema.CuentaEstandar())
                {
                    e.Motivo = "sin_permiso";
                    e.Error = "Esta cuenta no puede instalar drivers. Pide a quien administre el PC que escriba su contraseña cuando Windows la pida.";
                    e.Anotar("ERROR: " + e.Error);
                    e.Fase = "fallo";
                }
                else Terminar(new Cancelacion(Cancelado), null);
            }
            catch (Exception ex) { Terminar(ex, null); }
            finally { e.Trabajo = ""; e.Releer = true; }
        }

        // ------------------------------------------------------------ desbloquear

        public void Desbloquear(string serie)
        {
            try
            {
                var fb = new Fastboot(e.FB, serie);
                // Lo que tarda la persona en leer la pregunta de la Askey y contestar con los botones
                var confirmar = new Fastboot.Limites
                {
                    Total = TimeSpan.FromMinutes(2), Cancelar = Parar,
                    SiTiempo = "No se ha confirmado en la Askey. Si en su pantalla sale una pregunta, acéptala con los botones de volumen y encendido."
                };
                // Después de desbloquear, algunas se reinician en fastboot: se espera a que vuelva
                var volver = new Fastboot.Limites { Total = TimeSpan.FromSeconds(45), Espera = TimeSpan.FromSeconds(40), Cancelar = Parar };
                e.Contar("fastboot flashing unlock (mira la pantalla de la Askey)…");
                fb.Lanzar(new[] { "flashing", "unlock" }, l => e.Anotar("  " + l), confirmar);
                Thread.Sleep(2000);
                string u = fb.Variable("unlocked", volver);
                if (u != "yes")
                {
                    e.Contar("fastboot oem unlock (mira la pantalla de la Askey)…");
                    fb.Lanzar(new[] { "oem", "unlock" }, l => e.Anotar("  " + l), confirmar);
                    Thread.Sleep(2000);
                    u = fb.Variable("unlocked", volver);
                }
                if (u != "yes")
                    throw new Fallo("bloqueada", "Sigue bloqueado. Si en la Askey sale una pregunta, acéptala con los botones de volumen y encendido, y vuelve a pulsar Desbloquear.");
                e.Anotar("Bootloader desbloqueado.");
                e.Fase = "hecho";
            }
            catch (Cancelacion) { Terminar(new Cancelacion(DesbloqueoCancelado), null); }
            catch (Fallo f)
            {
                if (f.Motivo == "desconectada") Terminar(new Fallo("desconectada", "Se ha desconectado la Askey. Vuelve a conectarla en fastboot y pulsa Desbloquear."), null);
                else Terminar(f, null);
            }
            catch (Exception ex) { Terminar(ex, null); }
            finally { e.Trabajo = ""; e.Releer = true; }
        }

        // ------------------------------------------------------------ instalar

        // Única salida hacia fastboot que escribe: revisa cada orden antes de lanzarla
        void Ejecutar(Fastboot fb, string[] a, Action<string> cadaLinea)
        {
            bool vale = (a[0] == "flash" && Imagenes.Maximo.ContainsKey(a[1])) || (a[0] == "format" && (a[1] == "cache" || a[1] == "userdata"));
            if (!vale) throw new Fallo("negado", "NEGADO: fastboot " + string.Join(" ", a));
            e.Anotar("> fastboot " + a[0] + " " + a[1]);
            var lim = new Fastboot.Limites();
            var r = fb.Lanzar(a, l => { e.Anotar("  " + l); if (cadaLinea != null) cadaLinea(l); }, lim);
            if (r.Codigo != 0) throw Fastboot.Explicar(a[0] + " " + a[1], r.Error);
        }

        const string AntesDeGrabar = "Instalación detenida antes de grabar nada: la Askey sigue como estaba.";

        public void Instalar(string carpeta, string serie)
        {
            var pasos = new Pasos(e.Pasos);
            bool grabado = false;
            try
            {
                var fb = new Fastboot(e.FB, serie);

                // 1) Comprobar las imágenes
                pasos.Ir("imagenes");
                e.Progreso = -1;
                var lista = Imagenes.DelPaquete(carpeta);
                var pesos = new Dictionary<string, long>();
                foreach (var p in lista)
                {
                    e.Contar("Comprobando " + p.Key + ".img…");
                    Imagenes.Comprobar(carpeta, p.Key, p.Value, Parar);
                    pesos[p.Key] = new FileInfo(Path.Combine(carpeta, p.Key + ".img")).Length;
                }
                e.Anotar("Imágenes comprobadas.");

                // 2) El aparato sigue ahí, es el mismo y sigue desbloqueado
                pasos.Ir("askey");
                e.Contar("Comprobando la Askey…");
                string[] hay;
                try { hay = new Fastboot(e.FB).Aparatos(); }
                catch (Fallo) { hay = new string[0]; }
                if (hay.Length != 1 || hay[0] != serie)
                    throw new Fallo("conectar", "La Askey ya no está en fastboot (o hay otro aparato). No se ha grabado nada.");
                if (fb.Variable("unlocked") != "yes") throw new Fallo("bloqueada", "El bootloader está bloqueado. No se ha grabado nada.");

                // 3) Grabar, repartiendo la barra según el tamaño de cada imagen. Detener solo para entre una y otra
                const long formato = 64L << 20;
                double total = pesos.Values.Sum() + 2 * formato, hecho = 0;
                foreach (var par in lista)
                {
                    string p = par.Key;
                    if (Parar()) throw new Cancelacion(grabado ? InstalacionDetenida : AntesDeGrabar);
                    grabado = true;
                    pasos.Ir(p);
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
                    if (Parar()) throw new Cancelacion(InstalacionDetenida);
                    pasos.Ir(p);
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
            catch (Fallo f)
            {
                if (f.Motivo == "desconectada" || f.Motivo == "no_responde")
                    f = new Fallo(f.Motivo, f.Message + " Vuelve a conectarla en fastboot y pulsa Volver a intentarlo.");
                Terminar(f, pasos);
            }
            catch (Cancelacion) { Terminar(new Cancelacion(grabado ? InstalacionDetenida : AntesDeGrabar), pasos); }
            catch (Exception ex) { Terminar(ex, pasos); }
            finally { e.Trabajo = ""; }
        }
    }
}
