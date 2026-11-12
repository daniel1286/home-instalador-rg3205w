// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HomeInstalador
{
    // Una versión de Home: la de GitHub, la ya descargada o las dos
    class Version
    {
        public string Numero, Fecha, Carpeta;
        public bool Previa;
        public Release Remoto;
        public List<string> Cambios = new List<string>();
    }

    partial class Instalador
    {
        string descargas;
        Version version, instalando;
        List<Version> catalogo = new List<Version>();
        string pantalla = "", githubVisto = "", trabajoVisto = "", ficheroRegistro, firmaFases = "";
        bool mostrarFin, finBien;
        WrapPanel panelFases;

        // Versiones: las de GitHub más las ya descargadas
        static List<string> ListaCambios(string cuerpo)
        {
            var lineas = Regex.Split(cuerpo ?? "", "\r?\n");
            var sal = new List<string>();
            bool dentro = false;
            foreach (string l in lineas)
            {
                var m = Regex.Match(l, @"^#{2,4}\s*(.+)$");
                if (m.Success) { dentro = Regex.IsMatch(m.Groups[1].Value, "Qué trae|Novedades|Cambios|What"); continue; }
                if (l.StartsWith("---")) break;
                m = Regex.Match(l, @"^\s*[-*]\s+(.+)$");
                if (dentro && m.Success) sal.Add(Regex.Replace(m.Groups[1].Value, @"\*\*|`", ""));
            }
            if (sal.Count == 0)
            {
                string primera = lineas.FirstOrDefault(l => l.Trim().Length > 0 && !l.StartsWith("#"));
                if (primera != null) sal.Add(Regex.Replace(primera, @"\*\*|`", ""));
            }
            return sal;
        }

        static string Fecha(string iso)
        {
            DateTime d;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) ? d.ToLocalTime().ToString("dd-MM-yyyy") : "";
        }

        List<Version> LeerCatalogo()
        {
            var v = new List<Version>();
            if (demo != null)
            {
                if (e.Releases.Count == 0) return v;
                var r = e.Releases[0];
                v.Add(new Version { Numero = r.Numero, Fecha = Fecha(r.Fecha), Remoto = r, Cambios = ListaCambios(r.Notas), Carpeta = demoListo ? Rutas.Datos : null });
                return v;
            }
            foreach (var r in e.Releases)
                v.Add(new Version { Numero = r.Numero, Fecha = Fecha(r.Fecha), Remoto = r, Previa = r.Previa, Cambios = ListaCambios(r.Notas) });
            try
            {
                if (Directory.Exists(descargas))
                    foreach (var d in new DirectoryInfo(descargas).GetDirectories("Home-*-RG3205W"))
                    {
                        if (!Imagenes.CarpetaLista(d.FullName)) continue;
                        string num = Regex.Replace(d.Name, "^Home-(.+)-RG3205W$", "$1");
                        var ya = v.FirstOrDefault(x => x.Numero == num);
                        if (ya != null) ya.Carpeta = d.FullName;
                        else v.Add(new Version
                        {
                            Numero = num, Fecha = d.LastWriteTime.ToString("dd-MM-yyyy"), Carpeta = d.FullName,
                            Cambios = new List<string> { "Descargada en este PC. Las novedades salen cuando hay conexión." }
                        });
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return v;
        }

        void RellenarVersiones()
        {
            string antes = version != null ? version.Numero : null;
            catalogo = LeerCatalogo();
            var v = catalogo.FirstOrDefault(x => x.Numero == antes) ?? catalogo.FirstOrDefault(x => !x.Previa) ?? catalogo.FirstOrDefault();
            ElegirVersion(v);
        }

        string DatosVersion(Version v, bool largo)
        {
            var partes = new List<string> { v.Fecha };
            if (v.Previa) partes.Add("de prueba");
            if (v.Carpeta != null) partes.Add(largo ? "descargada en este PC" : "descargada");
            else if (v.Remoto != null) partes.Add(Imagenes.TamanoTexto(v.Remoto.Tamano) + (largo ? " en " + Red.Sitio(v.Remoto) : ""));
            return string.Join(" · ", partes.Where(p => !string.IsNullOrEmpty(p)));
        }

        void ElegirVersion(Version v)
        {
            version = v;
            VersionNumero.Text = v != null ? v.Numero : "—";
            VersionDatos.Text = v != null ? DatosVersion(v, false) : e.GitHub == "buscando" ? "buscando versiones…" : "sin versiones";
            pantalla = "";
        }

        // Los cuatro pasos de la izquierda
        class PasoRiel { public Border Circulo, Linea; public TextBlock Num, Titulo, Sub; public string Firma = ""; }
        readonly PasoRiel[] riel = new PasoRiel[5];

        void CrearRiel()
        {
            string[] nombres = { "Descargar", "Conectar", "Desbloquear", "Instalar" };
            for (int i = 1; i <= 4; i++)
            {
                var r = new PasoRiel();
                var fila = new DockPanel();
                var col = new Grid { Width = 30, Margin = new Thickness(0, 0, 14, 0) };
                DockPanel.SetDock(col, Dock.Left);
                r.Circulo = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Top };
                r.Num = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, FontWeight = FontWeights.Bold };
                r.Circulo.Child = r.Num;
                r.Linea = new Border { Width = 2, Margin = new Thickness(0, 34, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
                Pincel(r.Linea, Border.BackgroundProperty, "Borde");
                if (i < 4) col.Children.Add(r.Linea);
                col.Children.Add(r.Circulo);
                var textos = new StackPanel { Margin = new Thickness(0, 4, 0, 24) };
                r.Titulo = new TextBlock { Text = nombres[i - 1], FontSize = 15, FontWeight = FontWeights.SemiBold };
                r.Sub = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
                Pincel(r.Sub, TextBlock.ForegroundProperty, "Texto2");
                textos.Children.Add(r.Titulo);
                textos.Children.Add(r.Sub);
                fila.Children.Add(col);
                fila.Children.Add(textos);
                Riel.Children.Add(fila);
                riel[i] = r;
            }
        }

        // estado: hecho | actual | espera
        void PintarRiel(int i, string estado, string sub)
        {
            var r = riel[i];
            if (r.Firma == estado + "|" + sub) return;
            r.Firma = estado + "|" + sub;
            r.Sub.Text = sub;
            if (estado == "hecho")
            {
                Pincel(r.Circulo, Border.BorderBrushProperty, "Ok"); Pincel(r.Circulo, Border.BackgroundProperty, "Ok");
                Pincel(r.Num, FG, "TextoBoton"); r.Num.Text = "✓"; Pincel(r.Titulo, FG, "Texto"); Pincel(r.Linea, Border.BackgroundProperty, "Ok");
            }
            else if (estado == "actual")
            {
                Pincel(r.Circulo, Border.BorderBrushProperty, "Acento"); Pincel(r.Circulo, Border.BackgroundProperty, "AcentoSuave");
                Pincel(r.Num, FG, "Acento"); r.Num.Text = i.ToString(); Pincel(r.Titulo, FG, "Texto"); Pincel(r.Linea, Border.BackgroundProperty, "Borde");
            }
            else
            {
                Pincel(r.Circulo, Border.BorderBrushProperty, "Borde"); r.Circulo.Background = Brushes.Transparent;
                Pincel(r.Num, FG, "Texto2"); r.Num.Text = i.ToString(); Pincel(r.Titulo, FG, "Texto2"); Pincel(r.Linea, Border.BackgroundProperty, "Borde");
            }
        }

        // Registro: la caja de detalles y un .txt por trabajo
        void Registrar(string linea)
        {
            linea = Rutas.SinUsuario(linea ?? "");
            TextoRegistro.AppendText(linea + "\r\n");
            TextoRegistro.ScrollToEnd();
            if (ficheroRegistro == null) return;
            try { File.AppendAllText(ficheroRegistro, linea + "\r\n", Encoding.UTF8); }
            catch (IOException) { ficheroRegistro = null; }
        }

        void NuevoRegistro(string nombre)
        {
            try
            {
                Directory.CreateDirectory(Rutas.Registros);
                ficheroRegistro = System.IO.Path.Combine(Rutas.Registros, DateTime.Now.ToString("yyyy-MM-dd_HHmm") + "_" + nombre + ".txt");
            }
            catch (Exception) { ficheroRegistro = null; }
            TextoRegistro.Clear();
        }

        static string Ahora() { return DateTime.Now.ToString("dd-MM-yyyy HH:mm"); }

        // Pantallas: anillo, título, texto, contenido y un botón principal
        string PantallaActual()
        {
            if (mostrarFin) return finBien ? "fin_ok" : "fin_mal";
            switch (e.Trabajo)
            {
                case "instalar": return "instalando";
                case "descargar": return "descargando";
                case "desbloquear": return "desbloqueando";
                case "driver": return "driver";
            }
            if (version == null || version.Carpeta == null || string.IsNullOrEmpty(e.FB)) return "descargar";
            switch (e.Aparato.Estado)
            {
                case "ok": return "instalar";
                case "bloqueada": return "desbloquear";
                case "sin_driver": return "driver";
                case "edl": return "edl";
                case "no_valido": case "varios": case "error": return "otro";
                default: return "conectar";
            }
        }

        void Acciones(string principal, string accionP, string secundario, string accionS)
        {
            BotonPrincipal.Content = principal; BotonPrincipal.Tag = accionP; Visible(BotonPrincipal, principal != null);
            BotonSecundario.Content = secundario; BotonSecundario.Tag = accionS; Visible(BotonSecundario, secundario != null);
        }

        void MostrarPantalla(string k)
        {
            var v = version;
            var a = e.Aparato;
            Contenido.Children.Clear();
            Visible(CajaDetalles, false);
            Visible(Contenido, true);
            panelFases = null;
            switch (k)
            {
                case "descargar":
                    if (v == null && e.GitHub == "buscando")
                    {
                        PonerHeroe("giro", "descargar");
                        Titulo.Text = "Buscando la última versión"; Texto.Text = "Mirando las versiones de Home publicadas.";
                        Acciones(null, null, null, null);
                    }
                    else if (v == null)
                    {
                        PonerHeroe("aviso", "nube");
                        Titulo.Text = "Sin conexión"; Texto.Text = "Revisa la conexión a internet. Si ya tienes el zip de Home, puedes usarlo.";
                        Acciones("Reintentar", "reintentar", "Usar un zip que ya tengo", "zip");
                    }
                    else if (v.Carpeta != null)
                    {
                        PonerHeroe("espera", "descargar");
                        Titulo.Text = "Faltan las herramientas de Google";
                        Texto.Text = "Home " + v.Numero + " ya está descargado. Para grabarlo hace falta fastboot, que se baja de Google (8 MB).";
                        Acciones("Descargar fastboot", "descargar", null, null);
                    }
                    else
                    {
                        PonerHeroe("espera", "descargar");
                        Titulo.Text = "Descarga Home " + v.Numero;
                        Texto.Text = Imagenes.TamanoTexto(v.Remoto.Tamano) + " desde " + Red.Sitio(v.Remoto) + (string.IsNullOrEmpty(e.FB) ? ", con fastboot de Google" : "") +
                            ". Se comprueba la huella SHA256 antes de usarlo.";
                        var notas = v.Cambios.Take(3).Select(Corto).ToList();
                        if (notas.Count > 0)
                        {
                            var s = new StackPanel();
                            s.Children.Add(BloqueTexto("Qué trae", 12, "SemiBold", "Texto2", "0,0,0,6"));
                            s.Children.Add(ListaPuntos(notas, false));
                            Contenido.Children.Add(Tarjeta(s));
                        }
                        Acciones("Descargar Home " + v.Numero, "descargar", "¿La Askey tiene el sistema de fábrica? Empieza por la guía →", "guia:0");
                    }
                    break;
                case "descargando":
                    PonerHeroe("progreso", null);
                    Titulo.Text = "Descargando Home " + (v != null ? v.Numero : ""); Texto.Text = "";
                    Contenido.Children.Add(Tarjeta(BloqueTexto("Mientras baja, puedes ir preparando la Askey: abrirla, conectarla al PC y ponerla en modo fastboot.", 13, "Normal", "Texto2", "0")));
                    Contenido.Children.Add(Enlace("Abrir la guía →", "guia:1", HorizontalAlignment.Center, "0,10,0,0"));
                    Acciones(null, null, "Detener la descarga", "parar");
                    break;
                case "conectar":
                    PonerHeroe("espera", "enchufe");
                    Titulo.Text = "Conecta la Askey en modo fastboot";
                    Texto.Text = "Con el sistema de fábrica se entra con los botones de volumen y encendido. En cuanto la conectes por USB, se detecta sola.";
                    Contenido.Children.Add(Chip("Esperando a la Askey…", "Acento", true));
                    Acciones("Ver cómo se pone en fastboot", "guia:3", "Primero tengo que abrirla y hacer el cable →", "guia:1");
                    break;
                case "driver":
                    if (e.Trabajo == "driver")
                    {
                        PonerHeroe("giro", "enchufe");
                        Titulo.Text = "Instalando el driver USB"; Texto.Text = "Windows te pedirá permiso de administrador.";
                        Acciones(null, null, null, null);
                    }
                    else
                    {
                        PonerHeroe("aviso", "enchufe");
                        Titulo.Text = "Falta el driver USB";
                        Texto.Text = "Windows ve la Askey, pero necesita el driver de Google para hablar con ella. Se instala una sola vez.";
                        Acciones("Instalar el driver", "driver", "Prefiero hacerlo a mano →", "guia:2");
                    }
                    break;
                case "edl":
                    PonerHeroe("aviso", "aviso");
                    Titulo.Text = "La Askey está en modo EDL";
                    Texto.Text = "Es el modo de emergencia de Qualcomm, no el de instalar. Desenchúfala y entra en fastboot con los botones.";
                    Acciones("Ver cómo se pone en fastboot", "guia:3", null, null);
                    break;
                case "otro":
                    PonerHeroe("aviso", "aviso");
                    if (a.Estado == "no_valido") { Titulo.Text = "Este aparato no es un RG3205W"; Texto.Text = "Responde como «" + a.Variante + "». Este instalador solo vale para el Askey RG3205W."; }
                    else if (a.Estado == "varios") { Titulo.Text = "Hay más de un aparato"; Texto.Text = "Deja conectada solo la Askey en modo fastboot."; }
                    else { Titulo.Text = "Error al usar fastboot"; Texto.Text = a.Texto; }
                    Acciones(null, null, "Ver qué hacer →", "guia:7");
                    break;
                case "desbloquear":
                    PonerHeroe("espera", "cerrado");
                    Titulo.Text = "Desbloquea el bootloader";
                    Texto.Text = "Hace falta para instalar Home. Si en la pantalla de la Askey sale una pregunta, acéptala con los botones de volumen y encendido.";
                    Contenido.Children.Add(Chip("Askey " + a.Serie + " conectada", "Ok"));
                    Acciones("Desbloquear", "desbloquear", "Qué significa →", "guia:4");
                    break;
                case "desbloqueando":
                    PonerHeroe("giro", "abierto");
                    Titulo.Text = "Desbloqueando…"; Texto.Text = "Mira la pantalla de la Askey: si pregunta, acepta con los botones.";
                    Acciones(null, null, null, null);
                    break;
                case "instalar":
                    PonerHeroe("espera", "rayo");
                    Titulo.Text = "Todo listo";
                    Texto.Text = "Se graba Home y se borran todos los datos de la Askey. Tarda unos minutos.";
                    Contenido.Children.Add(Tarjeta(Resumen(new[]
                    {
                        new[] { "Sistema", "Home " + v.Numero },
                        new[] { "Askey", a.Serie + " · desbloqueada" },
                        new[] { "Se graba", "boot, recovery, vendor, system, splash" },
                        new[] { "Se borra", "todos los datos (cache y userdata)", "Aviso" }
                    })));
                    Acciones("Instalar Home " + v.Numero, "instalar", null, null);
                    break;
                case "instalando":
                    PonerHeroe("progreso", null);
                    Titulo.Text = "Instalando Home " + instalando.Numero; Texto.Text = "";
                    panelFases = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
                    Contenido.Children.Add(panelFases);
                    Contenido.Children.Add(Chip("No desconectes el USB ni cierres la ventana", "Aviso"));
                    Acciones(null, null, "Ver detalles", "detalles");
                    firmaFases = "";
                    break;
                case "fin_ok":
                    PonerHeroe("hecho", "hecho");
                    Titulo.Text = "Home " + instalando.Numero + " instalado";
                    Texto.Text = "Todo grabado y comprobado. El instalador no reinicia la Askey; hazlo tú así:";
                    Contenido.Children.Add(Tarjeta(ListaPuntos(new[] { "Desconecta el USB del PC.", "Desenchufa la Askey y vuelve a enchufarla.", "El primer arranque tarda varios minutos. Después sale el asistente." }, true)));
                    Acciones("Terminar", "terminar", "Qué viene después →", "guia:6");
                    break;
                case "fin_mal":
                    PonerHeroe("fallo", "fallo");
                    Titulo.Text = "No se ha terminado";
                    Texto.Text = e.Error;
                    Contenido.Children.Add(Chip("Si la Askey sigue en fastboot, puedes volver a intentarlo", "Aviso"));
                    if (informeTexto != null) Contenido.Children.Add(Enlace("Enviar informe del fallo →", "informe", HorizontalAlignment.Center, "0,16,0,0"));
                    Acciones("Volver a intentarlo", "otra_vez", "Ver detalles", "detalles");
                    break;
            }
            // Entrada: la pantalla llega deslizándose y aparece
            Animar(Pantalla, UIElement.OpacityProperty, 0, 1, 380);
            Animar(PantallaDesliz, TranslateTransform.XProperty, 28, 0, 420);
        }

        // Una novedad en una línea: hasta los dos puntos, o cortada por una palabra
        static string Corto(string t)
        {
            int dos = t.IndexOf(": ");
            if (dos > 20 && dos < 68) t = t.Substring(0, dos);
            if (t.Length <= 68) return t;
            int corte = t.LastIndexOf(' ', 66);
            return t.Substring(0, corte > 30 ? corte : 66).TrimEnd(',', ' ') + "…";
        }

        static string Porcentaje(double p) { return p.ToString("0") + "%"; }

        // Lo que cambia mientras se está en una pantalla (cifras, textos de progreso)
        void RefrescarPantalla(string k)
        {
            double p = e.Progreso;
            if (k == "descargando")
            {
                long total = e.Total, bytes = e.Bytes;
                double vel = e.Velocidad;
                if (total > 0 && e.Texto.StartsWith("Descargando"))
                {
                    arcoMeta = Math.Max(0, p) / 100;
                    HeroeCifra.Text = Porcentaje(Math.Max(0, p));
                    double resta = vel > 0 ? Math.Ceiling((total - bytes) / vel / 60) : 0;
                    Texto.Text = string.Format("{0} de {1} · {2}/s{3}", Imagenes.TamanoTexto(bytes), Imagenes.TamanoTexto(total),
                        Imagenes.TamanoTexto(vel), resta > 0 ? " · quedan " + resta + " min" : "");
                }
                else
                {
                    arcoMeta = p >= 0 ? p / 100 : 0;
                    HeroeCifra.Text = p >= 0 ? Porcentaje(p) : "···";
                    Texto.Text = e.Texto;
                }
            }
            else if (k == "instalando")
            {
                arcoMeta = Math.Max(0, p) / 100;
                HeroeCifra.Text = p < 0 ? "···" : Porcentaje(p);
                Texto.Text = e.Texto;
                var pasos = e.Pasos;
                if (panelFases != null && pasos != null)
                {
                    string firma = string.Join(",", pasos.Select(x => x.Estado));
                    if (firma != firmaFases) { firmaFases = firma; PintarFases(panelFases, pasos); }
                }
            }
            else if (k == "driver" && e.Trabajo == "driver" && e.Texto.Length > 0) Texto.Text = e.Texto;
        }

        // Refresco, 5 veces por segundo
        void Refrescar()
        {
            string l;
            while (e.Log.TryDequeue(out l)) Registrar(l);

            if (e.GitHub != githubVisto)
            {
                githubVisto = e.GitHub;
                if (githubVisto == "ok") { EstadoGitHub.Text = "en línea"; Pincel(PuntoGitHub, Ellipse.FillProperty, "Ok"); }
                else if (githubVisto == "sin_red") { EstadoGitHub.Text = "sin conexión"; Pincel(PuntoGitHub, Ellipse.FillProperty, "Aviso"); }
                else { EstadoGitHub.Text = "buscando…"; Pincel(PuntoGitHub, Ellipse.FillProperty, "Texto2"); }
                RellenarVersiones();
            }

            // Fin de un trabajo
            if ((e.Fase == "hecho" || e.Fase == "fallo") && e.Trabajo == "") TerminarTrabajo(e.Fase == "hecho");

            string k = PantallaActual();
            var a = e.Aparato;
            string firma = k + "|" + a.Estado + "|" + a.Serie + "|" + e.GitHub;
            if (firma != pantalla) { pantalla = firma; MostrarPantalla(k); }
            RefrescarPantalla(k);

            var v = version;
            bool listo = v != null && v.Carpeta != null && !string.IsNullOrEmpty(e.FB);
            bool conectada = a.Estado == "ok" || a.Estado == "bloqueada";
            bool acabado = mostrarFin && finBien;
            PintarRiel(1, listo ? "hecho" : "actual",
                listo ? "Home " + v.Numero + " listo" : k == "descargando" ? Math.Max(0, e.Progreso).ToString("0") + " %" : v != null ? "Home " + v.Numero : "");
            PintarRiel(2, conectada ? "hecho" : listo ? "actual" : "espera", conectada ? "Askey " + a.Serie : "Modo fastboot por USB");
            PintarRiel(3, a.Estado == "ok" ? "hecho" : listo && a.Estado == "bloqueada" ? "actual" : "espera", a.Estado == "ok" ? "Desbloqueado" : "Bootloader");
            PintarRiel(4, acabado ? "hecho" : listo && a.Estado == "ok" ? "actual" : "espera", acabado ? "Hecho" : "Graba y borra los datos");
            BotonMas.IsEnabled = e.Trabajo != "instalar";
            // Lo que se marcó al empezar es lo que vale para esta instalación
            CasillaEstadistica.IsEnabled = e.Trabajo != "instalar";
        }

        void TerminarTrabajo(bool bien)
        {
            Contar(bien);
            Registrar(bien ? "== Terminado bien." : "== Fallo: " + e.Error);
            PrepararInforme(bien);
            switch (trabajoVisto)
            {
                case "instalar":
                    mostrarFin = true;
                    finBien = bien;
                    break;
                case "descargar":
                    RellenarVersiones();
                    if (!bien && e.Error != Red.Detenida) AvisoFallo(e.Error, "Descarga");
                    break;
                case "driver":
                    if (bien) MessageBox.Show(Ventana, "Driver instalado. Si sigue diciendo «Falta el driver USB», desconecta y vuelve a conectar el USB o sigue el capítulo 3 de la guía.", "Driver USB", MessageBoxButton.OK, MessageBoxImage.Information);
                    else AvisoFallo(e.Error + "\n\nPuedes instalarlo a mano: capítulo 3 de la guía.", "Driver USB");
                    break;
                case "desbloquear":
                    if (!bien) AvisoFallo(e.Error, "Desbloquear");
                    break;
            }
            ficheroRegistro = null;
            e.Fase = "visto";
        }

        void Aviso(string texto, string titulo) { MessageBox.Show(Ventana, texto, titulo, MessageBoxButton.OK, MessageBoxImage.Warning); }

        // El aviso de un fallo, con la opción de enviar su informe (antes se ve qué se envía)
        void AvisoFallo(string texto, string titulo)
        {
            if (informeTexto == null) { Aviso(texto, titulo); return; }
            var r = MessageBox.Show(Ventana, texto + "\n\n¿Quieres enviar un informe de este fallo para que se pueda arreglar? Antes verás exactamente qué se envía.",
                titulo, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (r == MessageBoxResult.Yes) AbrirInforme();
        }

        // Modo demostración (INSTALADOR_DEMO = descargar | descargando | conectar | driver | desbloquear | instalar | instalando
        // | hecho | fallo | informe | guia | opciones): solo para revisar el diseño. No arranca el vigía, no toca fastboot y no
        // envía nada
        bool demoListo;

        void Demostracion()
        {
            Ventana.Title = "Instalar Home · DEMOSTRACIÓN";
            e.Releases = new List<Release>
            {
                new Release
                {
                    Numero = "2.0", Fecha = "2026-10-31T12:00:00Z", Zip = "Home-2.0-RG3205W.zip", Url = "https://github.com/" + Rutas.Repo + "/releases/download/2.0/Home-2.0-RG3205W.zip", Tamano = 1242161914, Sums = "", Huella = "",
                    Notas = "### Qué trae\n\n- Compilación `user` firmada con claves propias, SELinux estricto y parche de octubre de 2026.\n" +
                            "- Asistente de primer arranque: panel de Home Assistant, altavoz de casa o tablet.\n- App Actualizaciones: actualiza sin borrar datos.\n" +
                            "- Recovery TWRP con el diseño de Home.\n- Datos cifrados (FBE). Root con Magisk, apagado de serie."
                }
            };
            e.GitHub = "ok";
            if (string.IsNullOrEmpty(e.FB)) e.FB = "fastboot.exe";
            switch (demo)
            {
                case "descargar": case "descargando": case "conectar": case "guia": case "opciones": e.Aparato = new Aparato("ninguno"); break;
                case "driver": e.Aparato = new Aparato("sin_driver"); break;
                case "desbloquear": e.Aparato = new Aparato("bloqueada") { Serie = "a1b2c3d4", Variante = "Dragon eMMC" }; break;
                default: e.Aparato = new Aparato("ok") { Serie = "a1b2c3d4", Variante = "Dragon eMMC" }; break;
            }
            demoListo = demo != "descargar" && demo != "descargando";
            RellenarVersiones();
            if (demo == "descargando")
            {
                e.NuevasFases("herramientas", "descargar", "comprobar zip", "descomprimir", "comprobar imágenes");
                e.Trabajo = "descargar"; e.Texto = "Descargando Home-2.0-RG3205W.zip…";
                e.Total = 1242161914; e.Bytes = 583815099; e.Progreso = 47; e.Velocidad = 9.4 * 1048576;
            }
            if (demo == "guia") AbrirGuia(3);
            if (demo == "opciones") { PintarOpciones(); Visible(CapaOpciones, true); }
            instalando = version;
            if (demo == "instalando" || demo == "hecho" || demo == "fallo")
            {
                e.NuevasFases("SHA256", "boot", "recovery", "vendor", "system", "splash", "cache", "userdata");
                string[] estados = demo == "instalando" ? new[] { "hecho", "hecho", "hecho", "hecho", "en_curso", "espera", "espera", "espera" }
                    : demo == "hecho" ? Enumerable.Repeat("hecho", 8).ToArray()
                    : new[] { "hecho", "hecho", "fallo", "espera", "espera", "espera", "espera", "espera" };
                for (int i = 0; i < 8; i++) e.Pasos[i].Estado = estados[i];
                e.Progreso = 47; e.Texto = "Grabando system…";
                trabajoVisto = "instalar";
                if (demo == "instalando") { e.Trabajo = "instalar"; e.Fase = "en_curso"; }
                else { mostrarFin = true; finBien = demo == "hecho"; e.Error = "fastboot falló (1) en: fastboot flash recovery"; }
            }
            if (demo == "fallo" || demo == "informe")
            {
                informeHome = version.Numero; informePaso = "grabar_recovery";
                informeTexto = Estadistica.Limpiar("== Home 2.0 · Askey a1b2c3d4 · 12-11-2026 18:40\r\nSHA256 de boot.img, recovery.img, vendor.img, system.img y splash.img: correctas.\r\n" +
                    "> fastboot -s a1b2c3d4 flash boot boot.img\r\nSending 'boot' (16384 KB)    OKAY [  0.512s]\r\nWriting 'boot'    OKAY [  0.207s]\r\n" +
                    "> fastboot -s a1b2c3d4 flash recovery recovery.img\r\nSending 'recovery' (28672 KB)    FAILED (Write to device failed (Unknown error))\r\n" +
                    "fastboot: error: Command failed\r\n== Fallo: fastboot falló (1) en: fastboot flash recovery", "a1b2c3d4");
            }
            if (demo == "informe") AbrirInforme();
        }
    }
}
