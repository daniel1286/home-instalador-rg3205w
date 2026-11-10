// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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

        Ellipse PuntoGitHub, Pulso, Pulso2;
        TextBlock EstadoGitHub, VersionNumero, VersionDatos, HeroeCifra, Titulo, Texto;
        Button BotonGuia, BotonMas, BotonPrincipal, BotonSecundario, BotonCerrarGuia, BotonGuiaWeb, BotonCerrarOpciones;
        StackPanel Riel, Contenido, IndiceGuia, ContenidoGuia, ContenidoOpciones;
        Grid Pantalla, CapaGuia, CapaOpciones;
        TranslateTransform PantallaDesliz;
        ScaleTransform PulsoEscala, PulsoEscala2;
        RotateTransform ArcoGiro;
        Forma Arco, Icono;
        Viewbox CajaIcono;
        Border CajaDetalles;
        TextBox TextoRegistro;
        ScrollViewer ScrollGuia;

        public Instalador(string demo)
        {
            this.demo = string.IsNullOrEmpty(demo) ? null : demo;
            trabajos = new Trabajos(e);
            e.FB = Fastboot.Buscar();
            descargas = Rutas.Descargas();

            using (var s = Recurso("Ventana.xaml")) Ventana = (Window)XamlReader.Load(s);
            PuntoGitHub = C<Ellipse>("PuntoGitHub"); Pulso = C<Ellipse>("Pulso"); Pulso2 = C<Ellipse>("Pulso2");
            EstadoGitHub = C<TextBlock>("EstadoGitHub"); VersionNumero = C<TextBlock>("VersionNumero"); VersionDatos = C<TextBlock>("VersionDatos");
            HeroeCifra = C<TextBlock>("HeroeCifra"); Titulo = C<TextBlock>("Titulo"); Texto = C<TextBlock>("Texto");
            BotonGuia = C<Button>("BotonGuia"); BotonMas = C<Button>("BotonMas"); BotonPrincipal = C<Button>("BotonPrincipal");
            BotonSecundario = C<Button>("BotonSecundario"); BotonCerrarGuia = C<Button>("BotonCerrarGuia");
            BotonGuiaWeb = C<Button>("BotonGuiaWeb"); BotonCerrarOpciones = C<Button>("BotonCerrarOpciones");
            Riel = C<StackPanel>("Riel"); Contenido = C<StackPanel>("Contenido"); IndiceGuia = C<StackPanel>("IndiceGuia");
            ContenidoGuia = C<StackPanel>("ContenidoGuia"); ContenidoOpciones = C<StackPanel>("ContenidoOpciones");
            Pantalla = C<Grid>("Pantalla"); CapaGuia = C<Grid>("CapaGuia"); CapaOpciones = C<Grid>("CapaOpciones");
            PantallaDesliz = C<TranslateTransform>("PantallaDesliz");
            PulsoEscala = C<ScaleTransform>("PulsoEscala"); PulsoEscala2 = C<ScaleTransform>("PulsoEscala2");
            ArcoGiro = C<RotateTransform>("ArcoGiro"); Arco = C<Forma>("Arco"); Icono = C<Forma>("Icono");
            CajaIcono = C<Viewbox>("CajaIcono"); CajaDetalles = C<Border>("CajaDetalles");
            TextoRegistro = C<TextBox>("TextoRegistro"); ScrollGuia = C<ScrollViewer>("ScrollGuia");

            Aspecto();
            Anillo();
            CrearRiel();
            CrearGuia();
            Eventos();

            TextoRegistro.Text = "Aquí sale todo lo que se hace. Cada trabajo se guarda en la carpeta de registros.\r\n";
            RellenarVersiones();

            if (this.demo != null) Demostracion();
            else
            {
                Trabajos.Iniciar(trabajos.GitHub);
                Trabajos.Iniciar(trabajos.Vigia);
            }
            var reloj = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            reloj.Tick += delegate { Refrescar(); };
            reloj.Start();
        }

        T C<T>(string nombre) where T : class { return (T)Ventana.FindName(nombre); }

        static Stream Recurso(string nombre) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(nombre); }

        // Aspecto: los colores y la letra del asistente de Home
        static readonly Dictionary<string, string> Colores = new Dictionary<string, string>
        {
            { "Fondo", "#1B1A18" }, { "Riel", "#211F1C" }, { "Tarjeta", "#262420" }, { "Campo", "#2E2C29" }, { "Borde", "#37342F" },
            { "Texto", "#F3EFE8" }, { "Texto2", "#ABA59B" }, { "Acento", "#E0A85A" }, { "AcentoHover", "#EBBB78" },
            { "AcentoSuave", "#29E0A85A" }, { "AcentoTenue", "#80E0A85A" }, { "TextoBoton", "#2B1C06" }, { "Apagado", "#37342F" },
            { "Ok", "#9CCB8A" }, { "OkSuave", "#269CCB8A" }, { "Aviso", "#F08A6B" }, { "AvisoFondo", "#29F08A6B" }, { "Mal", "#F08A6B" }
        };

        void Aspecto()
        {
            foreach (var c in Colores)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Value));
                b.Freeze();
                Ventana.Resources[c.Key] = b;
            }
            Ventana.Resources["Letra"] = ExtraerLetras()
                ? new FontFamily(new Uri(Rutas.Letras + "\\"), "./#Inter, Segoe UI")
                : new FontFamily("Segoe UI");
            Ventana.SourceInitialized += delegate
            {
                // Barra de título oscura (Windows 10 20H1 y Windows 11)
                int uno = 1;
                DwmSetWindowAttribute(new WindowInteropHelper(Ventana).Handle, 20, ref uno, 4);
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
            catch (Exception) { return false; }
        }

        static void Pincel(FrameworkElement o, DependencyProperty p, string clave) { o.SetResourceReference(p, clave); }
        static void Pincel(FrameworkContentElement o, DependencyProperty p, string clave) { o.SetResourceReference(p, clave); }
        static readonly DependencyProperty FG = TextElement.ForegroundProperty;

        static void Visible(UIElement c, bool si) { c.Visibility = si ? Visibility.Visible : Visibility.Collapsed; }

        static void Animar(IAnimatable o, DependencyProperty p, double? desde, double hasta, int ms, int retraso = 0)
        {
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
            // El arco va hacia su meta poco a poco (cada 30 ms)
            var suave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            suave.Tick += delegate
            {
                double d = arcoMeta - arcoVisto;
                if (Math.Abs(d) < 0.0005) arcoVisto = arcoMeta; else arcoVisto += d * 0.16;
                PonerArco(arcoVisto);
            };
            suave.Start();
        }

        static void Latido(ScaleTransform escala, Ellipse elipse, int retraso, bool si)
        {
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
            if (si) ArcoGiro.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(1100))) { RepeatBehavior = RepeatBehavior.Forever });
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
            else if (modo == "hecho" || modo == "fallo") { arcoVisto = 0; arcoMeta = 1; }
            else arcoMeta = 0;
            Visible(HeroeCifra, modo == "progreso");
            Visible(CajaIcono, modo != "progreso");
            if (modo != "progreso")
            {
                Icono.Data = Geometry.Parse(Iconos[icono]);
                Animar(Icono, Shape.StrokeDashOffsetProperty, 60, 0, 900, 150);
            }
            HeroeCifra.Text = "";
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
            Ventana.Closing += Cerrando;
            Ventana.Closed += delegate { e.Cerrar = true; };
        }

        void Cerrando(object s, System.ComponentModel.CancelEventArgs a)
        {
            if (demo != null) return;
            string t = e.Trabajo;
            if (t == "instalar" || t == "desbloquear")
            {
                a.Cancel = true;
                MessageBox.Show(Ventana, "Espera a que acabe: cortar a medias deja la Askey sin sistema.", "Instalar Home", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (t == "descargar")
            {
                if (MessageBox.Show(Ventana, "Se está descargando Home. Lo bajado se guarda y sigue la próxima vez.\n\n¿Cerrar?", "Instalar Home",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) { a.Cancel = true; return; }
                e.Cancelar = true;
            }
        }
    }
}
