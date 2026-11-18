// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using Forma = System.Windows.Shapes.Path;

namespace HomeInstalador
{
    // Ventana: los cuatro pasos a la izquierda y, a la derecha, la pantalla del paso en el que se está
    partial class Instalador
    {
        public readonly Window Ventana;
        readonly Estado e = new Estado();
        readonly Trabajos trabajos;
        readonly string demo;

        Ellipse PuntoGitHub, Pulso, Pulso2, Brillo;
        TextBlock EstadoGitHub, VersionNumero, VersionDatos, HeroeCifra, Titulo, Texto, TituloDialogo;
        Button BotonGuia, BotonMas, BotonPrincipal, BotonSecundario, BotonCerrarGuia, BotonGuiaWeb, BotonCerrarOpciones, BotonQueSeEnvia, BotonCerrarInforme;
        CheckBox CasillaEstadistica;
        StackPanel Riel, Contenido, IndiceGuia, ContenidoGuia, ContenidoOpciones, ContenidoInforme, CuerpoDialogo, Botones;
        WrapPanel BotonesDialogo;
        Grid Raiz, Marco, Pantalla, Heroe, CapaGuia, CapaOpciones, CapaInforme, CapaDialogo;
        DockPanel MarcoGuia;
        Border MarcoPantalla, RielMarco, CajaDetalles, CajaVersion, TarjetaDialogo;
        ColumnDefinition ColumnaRiel;
        ScrollViewer ScrollPantalla, ScrollGuia, ScrollIndice;
        TranslateTransform PantallaDesliz;
        ScaleTransform PulsoEscala, PulsoEscala2;
        RotateTransform ArcoGiro;
        Forma Arco, Icono;
        Viewbox CajaIcono;
        TextBox TextoRegistro;

        public Instalador(string demo)
        {
            this.demo = string.IsNullOrEmpty(demo) ? null : demo;
            trabajos = new Trabajos(e);
            descargas = Rutas.Descargas();
            inicioSesion = DateTime.UtcNow;

            using (var s = Recurso("Ventana.xaml")) Ventana = (Window)XamlReader.Load(s);
            PuntoGitHub = C<Ellipse>("PuntoGitHub"); Pulso = C<Ellipse>("Pulso"); Pulso2 = C<Ellipse>("Pulso2"); Brillo = C<Ellipse>("Brillo");
            EstadoGitHub = C<TextBlock>("EstadoGitHub"); VersionNumero = C<TextBlock>("VersionNumero"); VersionDatos = C<TextBlock>("VersionDatos");
            HeroeCifra = C<TextBlock>("HeroeCifra"); Titulo = C<TextBlock>("Titulo"); Texto = C<TextBlock>("Texto");
            BotonGuia = C<Button>("BotonGuia"); BotonMas = C<Button>("BotonMas"); BotonPrincipal = C<Button>("BotonPrincipal");
            BotonSecundario = C<Button>("BotonSecundario"); BotonCerrarGuia = C<Button>("BotonCerrarGuia");
            BotonGuiaWeb = C<Button>("BotonGuiaWeb"); BotonCerrarOpciones = C<Button>("BotonCerrarOpciones");
            Riel = C<StackPanel>("Riel"); Contenido = C<StackPanel>("Contenido"); IndiceGuia = C<StackPanel>("IndiceGuia");
            ContenidoGuia = C<StackPanel>("ContenidoGuia"); ContenidoOpciones = C<StackPanel>("ContenidoOpciones");
            Raiz = C<Grid>("Raiz"); Marco = C<Grid>("Marco"); Pantalla = C<Grid>("Pantalla"); Heroe = C<Grid>("Heroe");
            CapaGuia = C<Grid>("CapaGuia"); CapaOpciones = C<Grid>("CapaOpciones");
            PantallaDesliz = C<TranslateTransform>("PantallaDesliz");
            PulsoEscala = C<ScaleTransform>("PulsoEscala"); PulsoEscala2 = C<ScaleTransform>("PulsoEscala2");
            ArcoGiro = C<RotateTransform>("ArcoGiro"); Arco = C<Forma>("Arco"); Icono = C<Forma>("Icono");
            CajaIcono = C<Viewbox>("CajaIcono"); CajaDetalles = C<Border>("CajaDetalles"); CajaVersion = C<Border>("CajaVersion");
            TextoRegistro = C<TextBox>("TextoRegistro"); ScrollGuia = C<ScrollViewer>("ScrollGuia"); ScrollIndice = C<ScrollViewer>("ScrollIndice");
            ScrollPantalla = C<ScrollViewer>("ScrollPantalla"); MarcoPantalla = C<Border>("MarcoPantalla"); Botones = C<StackPanel>("Botones");
            RielMarco = C<Border>("RielMarco"); MarcoGuia = C<DockPanel>("MarcoGuia"); ColumnaRiel = C<ColumnDefinition>("ColumnaRiel");
            CasillaEstadistica = C<CheckBox>("CasillaEstadistica"); BotonQueSeEnvia = C<Button>("BotonQueSeEnvia");
            CapaInforme = C<Grid>("CapaInforme"); ContenidoInforme = C<StackPanel>("ContenidoInforme"); BotonCerrarInforme = C<Button>("BotonCerrarInforme");
            CapaDialogo = C<Grid>("CapaDialogo"); TarjetaDialogo = C<Border>("TarjetaDialogo"); TituloDialogo = C<TextBlock>("TituloDialogo");
            CuerpoDialogo = C<StackPanel>("CuerpoDialogo"); BotonesDialogo = C<WrapPanel>("BotonesDialogo");
            CasillaEstadistica.IsChecked = Estadistica.Marcada;
            Ventana.TaskbarItemInfo = new TaskbarItemInfo();

            Aspecto();
            Medidas();
            Anillo();
            CrearRiel();
            CrearGuia();
            Eventos();

            TextoRegistro.Text = "Aquí sale todo lo que se hace. Cada trabajo se guarda en la carpeta de registros.\r\n";
            if (this.demo != null) Demostracion();
            else
            {
                // Siempre el fastboot propio, comprobado: si falta o lo han tocado, se vuelve a bajar
                string herr = Fastboot.Revisar();
                if (herr == "ok") { e.FB = Fastboot.Propio; e.Herr = "ok"; }
                else { e.FB = null; e.Herr = herr; if (herr != "falta") Registrar("Herramientas de Google: " + herr + "."); }
                RellenarVersiones();
                Trabajos.Iniciar(trabajos.BuscarVersiones);
                Trabajos.Iniciar(trabajos.Vigia);
            }
            var reloj = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            reloj.Tick += delegate { Refrescar(); };
            reloj.Start();
        }

        T C<T>(string nombre) where T : class { return (T)Ventana.FindName(nombre); }

        static Stream Recurso(string nombre) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(nombre); }

        // ------------------------------------------------------------ aspecto

        // Los colores y la letra del asistente de Home
        static readonly Dictionary<string, string> Colores = new Dictionary<string, string>
        {
            { "Fondo", "#1B1A18" }, { "Riel", "#211F1C" }, { "Tarjeta", "#262420" }, { "Campo", "#2E2C29" }, { "Borde", "#37342F" },
            { "Texto", "#F3EFE8" }, { "Texto2", "#ABA59B" }, { "Acento", "#E0A85A" }, { "AcentoHover", "#EBBB78" },
            { "AcentoSuave", "#29E0A85A" }, { "AcentoTenue", "#80E0A85A" }, { "TextoBoton", "#2B1C06" }, { "Apagado", "#37342F" },
            { "Ok", "#9CCB8A" }, { "OkSuave", "#269CCB8A" }, { "Aviso", "#F08A6B" }, { "AvisoHover", "#F4A189" }, { "AvisoFondo", "#29F08A6B" },
            { "Mal", "#F08A6B" }, { "TextoPeligro", "#2B0E06" }, { "Velo", "#B3100F0E" }
        };

        // Con contraste alto de Windows, los colores del sistema en vez de los propios
        static Color DeContraste(string clave)
        {
            switch (clave)
            {
                case "Texto": case "Texto2": return SystemColors.WindowTextColor;
                case "Borde": case "Apagado": return SystemColors.GrayTextColor;
                case "Acento": case "AcentoHover": case "Ok": case "Aviso": case "AvisoHover": case "Mal": case "AcentoTenue": return SystemColors.HighlightColor;
                case "TextoBoton": case "TextoPeligro": return SystemColors.HighlightTextColor;
                case "Velo": return Color.FromArgb(0xB3, 0, 0, 0);
                default: return SystemColors.WindowColor;
            }
        }

        static bool contraste;
        // Sin animaciones continuas ni desenfoques si Windows las tiene apagadas, en escritorio remoto o con contraste alto
        static bool animar = true;
        static double escalaTexto = 1;

        // Tamaño de letra: nunca por debajo de 12 y siguiendo «Tamaño del texto» de Windows. Como en Android, la letra
        // pequeña crece del todo y la grande menos, para que los títulos no se salgan
        public static double Tam(double t)
        {
            t = Math.Max(12, t);
            double peso = Math.Max(0.35, Math.Min(1, (40 - t) / 27));
            return Math.Round(t * (1 + (escalaTexto - 1) * peso));
        }

        static readonly double[] Tamanos = { 12, 13, 14, 15, 16, 20, 30, 38 };

        void Aspecto()
        {
            contraste = SystemParameters.HighContrast;
            animar = SystemParameters.ClientAreaAnimation && !System.Windows.Forms.SystemInformation.TerminalServerSession && !contraste;
            escalaTexto = Sistema.EscalaTexto();
            foreach (var c in Colores)
            {
                var b = new SolidColorBrush(contraste ? DeContraste(c.Key) : (Color)ColorConverter.ConvertFromString(c.Value));
                b.Freeze();
                Ventana.Resources[c.Key] = b;
            }
            foreach (double t in Tamanos) Ventana.Resources["T" + t] = Tam(t);
            Ventana.Resources["TTitulo"] = Tam(28);
            Ventana.Resources["AltoBoton"] = 52.0;
            Ventana.Resources["Sombra"] = animar ? Visibility.Visible : Visibility.Collapsed;
            if (!animar) Visible(Brillo, false);
            Ventana.Resources["Letra"] = ExtraerLetras()
                ? new FontFamily(new Uri(Rutas.Letras + "\\"), "./#Inter, Segoe UI")
                : new FontFamily("Segoe UI");
            Ventana.SourceInitialized += delegate
            {
                // Barra de título oscura (Windows 10 20H1 y Windows 11), salvo con contraste alto
                int uno = 1;
                if (!contraste) DwmSetWindowAttribute(new WindowInteropHelper(Ventana).Handle, 20, ref uno, 4);
            };
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int atributo, ref int valor, int tam);

        // WPF solo carga letras de un archivo: las de dentro del .exe se copian a la carpeta del instalador
        static bool ExtraerLetras()
        {
            try
            {
                Directory.CreateDirectory(Rutas.Letras);
                foreach (string n in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    if (!n.StartsWith("letras.")) continue;
                    string destino = System.IO.Path.Combine(Rutas.Letras, n.Substring(7));
                    using (var s = Recurso(n))
                    {
                        if (File.Exists(destino) && new FileInfo(destino).Length == s.Length) continue;
                        using (var f = File.Create(destino)) s.CopyTo(f);
                    }
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // ------------------------------------------------------------ medidas

        // Tamaño inicial que cabe en el escritorio (sin la barra de tareas) y modo compacto cuando hay poco alto o ancho:
        // así se ve igual de bien del 100 al 175 % de escala y en pantallas de 1366×768
        void Medidas()
        {
            var area = SystemParameters.WorkArea;
            Ventana.Width = Math.Max(Ventana.MinWidth, Math.Min(1100, area.Width * 0.95));
            Ventana.Height = Math.Max(Ventana.MinHeight, Math.Min(700, area.Height * 0.95));
            Raiz.SizeChanged += delegate { Adaptar(); };
            ScrollPantalla.SizeChanged += delegate { AjustarDetalles(); };
            TarjetaDialogo.MaxHeight = Math.Max(300, Ventana.Height - 80);
        }

        // compacto: poco alto para el anillo grande. justo: poco alto para el riel holgado. estrecho: poco ancho
        bool compacto, justo, estrecho, adaptado;

        void Adaptar()
        {
            bool c = Raiz.ActualHeight < 620, j = Raiz.ActualHeight < 740, n = Raiz.ActualWidth < 960;
            TarjetaDialogo.MaxHeight = Math.Max(240, Raiz.ActualHeight - 48);
            if (adaptado && c == compacto && j == justo && n == estrecho) { AjustarDetalles(); return; }
            adaptado = true; compacto = c; justo = j; estrecho = n;
            Heroe.LayoutTransform = compacto ? new ScaleTransform(0.68, 0.68) : (Transform)Transform.Identity;
            Heroe.Margin = new Thickness(0, 0, 0, compacto ? 12 : 22);
            double lado = estrecho ? 28 : 56;
            MarcoPantalla.Padding = compacto ? new Thickness(lado, 16, lado, 16) : new Thickness(lado, 36, lado, 30);
            ColumnaRiel.Width = new GridLength(estrecho ? 240 : 290);
            RielMarco.Padding = compacto || estrecho ? new Thickness(20, 18, 18, 14) : justo ? new Thickness(30, 22, 26, 16) : new Thickness(30, 30, 26, 24);
            CajaVersion.Margin = new Thickness(0, compacto ? 12 : justo ? 16 : 26, 0, 0);
            Riel.Margin = new Thickness(0, compacto ? 14 : justo ? 20 : 30, 0, 0);
            foreach (var r in riel) if (r != null) r.Textos.Margin = new Thickness(0, 4, 0, compacto ? 10 : justo ? 14 : 24);
            Ventana.Resources["TTitulo"] = Tam(compacto ? 22 : 28);
            Ventana.Resources["T38"] = Tam(compacto ? 30 : 38);
            Ventana.Resources["AltoBoton"] = compacto ? 44.0 : 52.0;
            MarcoGuia.Margin = compacto ? new Thickness(18, 14, 14, 12) : new Thickness(28, 24, 24, 20);
            ScrollIndice.Width = estrecho ? 160 : 196;
            AjustarDetalles();
        }

        // La caja de detalles ocupa lo que queda entre el texto y los botones (con un mínimo; si no cabe, se desplaza)
        void AjustarDetalles()
        {
            if (CajaDetalles.Visibility != Visibility.Visible) return;
            Ventana.Dispatcher.BeginInvoke(new Action(delegate
            {
                double otros = MarcoPantalla.Padding.Top + MarcoPantalla.Padding.Bottom + Heroe.ActualHeight + Heroe.Margin.Bottom
                    + Titulo.ActualHeight + Texto.ActualHeight + Texto.Margin.Top + 18 + Botones.ActualHeight + Botones.Margin.Top;
                CajaDetalles.Height = Math.Max(140, ScrollPantalla.ViewportHeight - otros - 4);
            }), DispatcherPriority.Loaded);
        }

        // ------------------------------------------------------------ piezas que se mueven

        static void Pincel(FrameworkElement o, DependencyProperty p, string clave) { o.SetResourceReference(p, clave); }
        static void Pincel(FrameworkContentElement o, DependencyProperty p, string clave) { o.SetResourceReference(p, clave); }
        static readonly DependencyProperty FG = TextElement.ForegroundProperty;

        static void Visible(UIElement c, bool si) { c.Visibility = si ? Visibility.Visible : Visibility.Collapsed; }

        static void Animar(IAnimatable o, DependencyProperty p, double? desde, double hasta, int ms, int retraso = 0)
        {
            if (!animar) { o.BeginAnimation(p, null); ((DependencyObject)o).SetValue(p, hasta); return; }
            var a = new DoubleAnimation(hasta, new Duration(TimeSpan.FromMilliseconds(ms)))
            {
                BeginTime = TimeSpan.FromMilliseconds(retraso),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            if (desde.HasValue) a.From = desde.Value;
            o.BeginAnimation(p, a);
        }

        // Anillo del centro e iconos (trazos en una caja de 48×48)
        static readonly Dictionary<string, string> Iconos = new Dictionary<string, string>
        {
            { "descargar", "M24 7 V31 M14 21 L24 31 L34 21 M11 40 H37" },
            { "enchufe", "M19 5 V13 M29 5 V13 M14 13 H34 V21 A10 10 0 0 1 24 31 A10 10 0 0 1 14 21 Z M24 31 V43" },
            { "cerrado", "M15 22 V16 A9 9 0 0 1 33 16 V22 M11 22 H37 V42 H11 Z M24 30 V34" },
            { "abierto", "M15 22 V16 A9 9 0 0 1 32 12 M11 22 H37 V42 H11 Z M24 30 V34" },
            { "rayo", "M27 5 L13 27 H23 L21 43 L35 20 H25 Z" },
            { "hecho", "M12 25 L21 34 L37 15" },
            { "fallo", "M16 16 L32 32 M32 16 L16 32" },
            { "aviso", "M24 6 L43 39 H5 Z M24 18 V28 M24 33 V34" },
            { "nube", "M14 34 A8 8 0 0 1 14 18 A11 11 0 0 1 35 16 A9 9 0 0 1 35 34 Z M17 41 L31 27" }
        };
        const double Radio = 88, Centro = 98;
        double arcoMeta, arcoVisto;

        void PonerArco(double f)
        {
            if (f <= 0.001) { Arco.Data = null; return; }
            f = Math.Min(f, 0.9999);
            double ang = 2 * Math.PI * f;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(Centro, Centro - Radio), false, false);
                c.ArcTo(new Point(Centro + Radio * Math.Sin(ang), Centro - Radio * Math.Cos(ang)), new Size(Radio, Radio), 0, f > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            Arco.Data = g;
        }

        void Anillo()
        {
            // El arco va hacia su meta poco a poco (cada 30 ms); sin animaciones, de golpe
            var suave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            suave.Tick += delegate
            {
                double d = arcoMeta - arcoVisto;
                if (Math.Abs(d) < 0.0005) { if (arcoVisto == arcoMeta) return; arcoVisto = arcoMeta; }
                else arcoVisto = animar ? arcoVisto + d * 0.16 : arcoMeta;
                PonerArco(arcoVisto);
            };
            suave.Start();
        }

        static void Latido(ScaleTransform escala, Ellipse elipse, int retraso, bool si)
        {
            si = si && animar;
            foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                if (!si) { escala.BeginAnimation(p, null); continue; }
                escala.BeginAnimation(p, new DoubleAnimation(0.92, 1.32, new Duration(TimeSpan.FromMilliseconds(2400)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(retraso),
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                });
            }
            if (!si) { elipse.BeginAnimation(UIElement.OpacityProperty, null); elipse.Opacity = 0; return; }
            elipse.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.7, 0, new Duration(TimeSpan.FromMilliseconds(2400)))
            {
                BeginTime = TimeSpan.FromMilliseconds(retraso),
                RepeatBehavior = RepeatBehavior.Forever
            });
        }

        void Giro(bool si)
        {
            if (si && animar) ArcoGiro.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(1100))) { RepeatBehavior = RepeatBehavior.Forever });
            else { ArcoGiro.BeginAnimation(RotateTransform.AngleProperty, null); ArcoGiro.Angle = 0; }
        }

        // modo: espera (late) | giro (trabaja sin saber cuánto) | progreso (se llena) | hecho | fallo | aviso
        void PonerHeroe(string modo, string icono)
        {
            string color = modo == "hecho" ? "Ok" : modo == "fallo" || modo == "aviso" ? "Aviso" : "Acento";
            foreach (var o in new Shape[] { Arco, Icono, Pulso, Pulso2 }) Pincel(o, Shape.StrokeProperty, color);
            bool late = modo == "espera" || modo == "aviso";
            Latido(PulsoEscala, Pulso, 0, late);
            Latido(PulsoEscala2, Pulso2, 1200, late);
            Giro(modo == "giro");
            if (modo == "giro") { arcoVisto = 0.22; arcoMeta = 0.22; }
            else if (modo == "progreso") { arcoVisto = 0; arcoMeta = 0; }
            else if (modo == "hecho" || modo == "fallo") { arcoVisto = animar ? 0 : 1; arcoMeta = 1; }
            else arcoMeta = 0;
            PonerArco(arcoVisto);
            Visible(HeroeCifra, modo == "progreso");
            Visible(CajaIcono, modo != "progreso");
            if (modo != "progreso")
            {
                Icono.Data = Geometry.Parse(Iconos[icono]);
                Animar(Icono, Shape.StrokeDashOffsetProperty, 60, 0, 900, 150);
            }
            HeroeCifra.Text = "";
            AutomationProperties.SetName(Heroe, modo == "progreso" ? "Progreso" : modo == "hecho" ? "Terminado" : modo == "fallo" ? "Fallo" : modo == "aviso" ? "Aviso" : "En espera");
        }

        // Los lectores de pantalla leen el título y el texto cuando cambia la pantalla
        static void Anunciar(UIElement u)
        {
            var peer = UIElementAutomationPeer.FromElement(u) ?? UIElementAutomationPeer.CreatePeerForElement(u);
            if (peer != null) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // Barra de tareas: el progreso de la descarga y de la instalación, y en rojo si ha fallado
        void PonerBarra(string k)
        {
            var b = Ventana.TaskbarItemInfo;
            double p = e.Progreso;
            TaskbarItemProgressState estado;
            if (k == "descargando" || k == "instalando") estado = p >= 0 ? TaskbarItemProgressState.Normal : TaskbarItemProgressState.Indeterminate;
            else if (k == "desbloqueando" || (k == "driver" && e.Trabajo == "driver")) estado = TaskbarItemProgressState.Indeterminate;
            else if (k == "fin_mal") estado = TaskbarItemProgressState.Error;
            else estado = TaskbarItemProgressState.None;
            if (b.ProgressState != estado) b.ProgressState = estado;
            if (estado == TaskbarItemProgressState.Normal) b.ProgressValue = Math.Max(0, Math.Min(100, p)) / 100;
            else if (estado == TaskbarItemProgressState.Error) b.ProgressValue = 1;
        }

        void Eventos()
        {
            BotonPrincipal.Click += delegate { Accion(BotonPrincipal.Tag as string); };
            BotonSecundario.Click += delegate { Accion(BotonSecundario.Tag as string); };
            BotonGuia.Click += delegate { AbrirGuia(-1); };
            BotonCerrarGuia.Click += delegate { Visible(CapaGuia, false); };
            BotonGuiaWeb.Click += delegate { Abrir(Rutas.GuiaWeb); };
            BotonMas.Click += delegate
            {
                Visible(CapaGuia, false);
                PintarOpciones();
                Visible(CapaOpciones, true);
                Animar(CapaOpciones, UIElement.OpacityProperty, 0, 1, 250);
            };
            BotonCerrarOpciones.Click += delegate { Visible(CapaOpciones, false); };
            CasillaEstadistica.Checked += delegate { if (demo == null) Estadistica.Marcada = true; };
            CasillaEstadistica.Unchecked += delegate { if (demo == null) Estadistica.Marcada = false; };
            BotonQueSeEnvia.Click += delegate { QueSeEnvia(); };
            BotonCerrarInforme.Click += delegate { Visible(CapaInforme, false); };
            Ventana.PreviewKeyDown += Tecla;
            Ventana.Closing += Cerrando;
            Ventana.Closed += delegate
            {
                e.Cerrar = true;
                Fastboot.PararTodos();
                Abandono();
            };
        }

        // Esc cierra el aviso o la capa que esté abierta; Intro, en un aviso, es su botón principal
        void Tecla(object s, KeyEventArgs a)
        {
            if (CapaDialogo.Visibility == Visibility.Visible)
            {
                if (a.Key == Key.Escape) { Responder(dialogo != null ? dialogo.Cancelar : -1); a.Handled = true; }
                return;
            }
            if (a.Key != Key.Escape) return;
            foreach (var c in new[] { CapaInforme, CapaOpciones, CapaGuia })
                if (c.Visibility == Visibility.Visible) { Visible(c, false); a.Handled = true; return; }
        }

        bool cerrarYa;

        // La ventana siempre se puede cerrar. A mitad de instalar se avisa de lo que pasa y, si se confirma, se para fastboot
        void Cerrando(object s, System.ComponentModel.CancelEventArgs a)
        {
            if (demo != null || cerrarYa) return;
            string t = e.Trabajo;
            if (t == "instalar")
            {
                a.Cancel = true;
                Preguntar("¿Cerrar a mitad de la instalación?",
                    "Si cierras ahora, se corta lo que se está grabando y la Askey se queda **sin un sistema completo**: no arrancará hasta que vuelvas a instalar Home.",
                    "Cerrar de todos modos", "Seguir instalando", true, CerrarAhora);
                return;
            }
            if (t == "desbloquear")
            {
                a.Cancel = true;
                Preguntar("¿Cerrar sin desbloquear?", "Se cancela el desbloqueo. Si en la pantalla de la Askey hay una pregunta, déjala sin contestar.",
                    "Cerrar", "Seguir", false, CerrarAhora);
                return;
            }
            if (t == "descargar")
            {
                a.Cancel = true;
                Preguntar("¿Cerrar la descarga?", "Se está descargando Home. Lo bajado se guarda y sigue la próxima vez que lo abras.",
                    "Cerrar", "Seguir descargando", false, CerrarAhora);
            }
            // Con el driver no hace falta preguntar: lo asigna un proceso de Windows aparte, que termina aunque se cierre
        }

        void CerrarAhora()
        {
            cerrarYa = true;
            e.Cancelar = true;
            Fastboot.PararTodos();
            Ventana.Close();
        }

        // ------------------------------------------------------------ avisos y preguntas

        class Dialogo
        {
            public string Titulo;
            public UIElement Cuerpo;
            public string[] Botones;
            public int Principal, Cancelar = -1;
            public bool Peligro;
            public Action<int> Respuesta;
        }

        readonly Queue<Dialogo> pendientes = new Queue<Dialogo>();
        Dialogo dialogo;
        IInputElement focoAntes;

        void Mostrar(Dialogo d)
        {
            if (dialogo != null) { pendientes.Enqueue(d); return; }
            dialogo = d;
            focoAntes = Keyboard.FocusedElement;
            TituloDialogo.Text = d.Titulo;
            CuerpoDialogo.Children.Clear();
            CuerpoDialogo.Children.Add(d.Cuerpo);
            BotonesDialogo.Children.Clear();
            Button principal = null;
            // De derecha a izquierda: el principal, a la derecha
            for (int i = d.Botones.Length - 1; i >= 0; i--)
            {
                int n = i;
                bool esPrincipal = i == d.Principal;
                var b = new Button
                {
                    Content = d.Botones[i],
                    Style = (Style)Ventana.FindResource(esPrincipal ? (d.Peligro ? "Peligro" : "Principal") : "Enlace"),
                    Margin = new Thickness(esPrincipal ? 18 : 0, 6, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (esPrincipal) { b.MinWidth = 150; principal = b; b.IsDefault = true; }
                if (i == d.Cancelar) b.IsCancel = true;
                b.Click += delegate { Responder(n); };
                BotonesDialogo.Children.Insert(0, b);
            }
            BotonPrincipal.IsDefault = false;
            Visible(CapaDialogo, true);
            Marco.IsHitTestVisible = false;
            KeyboardNavigation.SetTabNavigation(Marco, KeyboardNavigationMode.None);
            Animar(CapaDialogo, UIElement.OpacityProperty, 0, 1, 180);
            Ventana.Dispatcher.BeginInvoke(new Action(delegate { if (principal != null) principal.Focus(); }), DispatcherPriority.Loaded);
            Anunciar(TituloDialogo);
        }

        void Responder(int n)
        {
            var d = dialogo;
            if (d == null) return;
            dialogo = null;
            Visible(CapaDialogo, false);
            Marco.IsHitTestVisible = true;
            KeyboardNavigation.SetTabNavigation(Marco, KeyboardNavigationMode.Continue);
            BotonPrincipal.IsDefault = true;
            if (focoAntes != null) Keyboard.Focus(focoAntes);
            if (pendientes.Count > 0) Mostrar(pendientes.Dequeue());
            if (d.Respuesta != null) d.Respuesta(n);
        }

        UIElement CuerpoTexto(string texto)
        {
            var s = new StackPanel();
            foreach (string parrafo in (texto ?? "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                s.Children.Add(BloqueTexto(parrafo.Trim(), 14, "Normal", "Texto2", "0,0,0,10"));
            return s;
        }

        // Un aviso con un botón (y, si hace falta, una segunda acción)
        public void Avisar(string titulo, string texto, string otro = null, Action alOtro = null)
        {
            var botones = otro != null ? new[] { otro, "Entendido" } : new[] { "Entendido" };
            Mostrar(new Dialogo
            {
                Titulo = titulo, Cuerpo = CuerpoTexto(texto), Botones = botones, Principal = botones.Length - 1, Cancelar = botones.Length - 1,
                Respuesta = n => { if (otro != null && n == 0 && alOtro != null) alOtro(); }
            });
        }

        // Una pregunta: «si» hace la acción (en color de aviso si no tiene vuelta atrás) y «no» la deja como estaba
        void Preguntar(string titulo, string texto, string si, string no, bool peligro, Action alAceptar, UIElement extra = null)
        {
            var cuerpo = (StackPanel)CuerpoTexto(texto);
            if (extra != null) cuerpo.Children.Add(extra);
            Mostrar(new Dialogo
            {
                Titulo = titulo, Cuerpo = cuerpo, Botones = new[] { no, si }, Principal = 1, Cancelar = 0, Peligro = peligro,
                Respuesta = n => { if (n == 1) alAceptar(); }
            });
        }

        // Sin la ventana principal (no ha llegado a abrirse): un aviso suelto con el mismo aspecto
        public static void Suelto(string titulo, string texto)
        {
            var fondo = new SolidColorBrush(Color.FromRgb(0x21, 0x1F, 0x1C));
            var w = new Window
            {
                Title = titulo, Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = fondo, FontFamily = new FontFamily("Segoe UI")
            };
            var s = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
            s.Children.Add(new TextBlock { Text = titulo, FontSize = 19, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0xEF, 0xE8)), Margin = new Thickness(0, 0, 0, 10) });
            s.Children.Add(new TextBlock { Text = texto, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0xAB, 0xA5, 0x9B)) });
            var b = new Button
            {
                Content = "Entendido", IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0),
                Padding = new Thickness(22, 8, 22, 8), FontSize = 14, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xA8, 0x5A)), Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x1C, 0x06)), BorderThickness = new Thickness(0)
            };
            b.Click += delegate { w.Close(); };
            s.Children.Add(b);
            w.Content = s;
            w.SourceInitialized += delegate
            {
                int uno = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 20, ref uno, 4);
            };
            w.ShowDialog();
        }
    }
}
