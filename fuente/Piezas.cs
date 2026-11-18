// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HomeInstalador
{
    // Piezas de las pantallas, de la guía y de las opciones
    partial class Instalador
    {
        // Texto con **negritas**. El tamaño pasa por Tam: nunca menos de 12 y con el tamaño del texto de Windows
        static TextBlock BloqueTexto(string texto, double tam = 13, string peso = "Normal", string color = "Texto", string margen = "0,0,0,10")
        {
            double t = Tam(tam);
            var b = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = t,
                FontWeight = peso == "Bold" ? FontWeights.Bold : peso == "SemiBold" ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = Margen(margen),
                LineHeight = t * 1.45
            };
            foreach (string p in Regex.Split(texto ?? "", @"(\*\*[^*]+\*\*)"))
            {
                if (p.Length == 0) continue;
                var m = Regex.Match(p, @"^\*\*(.+)\*\*$");
                var r = new Run(m.Success ? m.Groups[1].Value : p);
                if (m.Success) { r.FontWeight = FontWeights.SemiBold; Pincel(r, FG, color == "Texto2" ? "Texto" : color); }
                b.Inlines.Add(r);
            }
            Pincel(b, FG, color);
            return b;
        }

        static Thickness Margen(string m) { return (Thickness)new ThicknessConverter().ConvertFromInvariantString(m); }

        static Border Tarjeta(UIElement hijo)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 14, 20, 14), Child = hijo };
            Pincel(b, Border.BackgroundProperty, "Tarjeta");
            return b;
        }

        static Border Numero(int i, string margen)
        {
            double lado = Math.Max(22, Tam(12) * 1.8);
            var n = new Border { Width = lado, Height = lado, CornerRadius = new CornerRadius(lado / 2), Margin = Margen(margen), VerticalAlignment = VerticalAlignment.Top };
            Pincel(n, Border.BackgroundProperty, "Acento");
            var t = new TextBlock { Text = i.ToString(), FontSize = Tam(12), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Pincel(t, FG, "TextoBoton");
            n.Child = t;
            return n;
        }

        // Lista con puntos ámbar (novedades) o con números (instrucciones)
        static StackPanel ListaPuntos(IEnumerable<string> lineas, bool numeros)
        {
            var s = new StackPanel();
            int i = 1;
            foreach (string l in lineas)
            {
                var d = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                FrameworkElement m;
                if (numeros) m = Numero(i, "0,0,12,0");
                else
                {
                    m = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, Tam(14) * 0.5, 12, 0), VerticalAlignment = VerticalAlignment.Top };
                    Pincel(m, Shape.FillProperty, "Acento");
                }
                DockPanel.SetDock(m, Dock.Left);
                d.Children.Add(m);
                d.Children.Add(BloqueTexto(l, 14, "Normal", "Texto", "0,1,0,0"));
                s.Children.Add(d);
                i++;
            }
            return s;
        }

        // Filas «nombre · valor» del resumen antes de instalar
        static Grid Resumen(string[][] filas)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(110, Tam(13) * 8.5)) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < filas.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition());
                var a = BloqueTexto(filas[i][0], 13, "Normal", "Texto2", "0,4,8,4");
                var b = BloqueTexto(filas[i][1], 14, "SemiBold", filas[i].Length > 2 ? filas[i][2] : "Texto", "0,4,0,4");
                Grid.SetRow(a, i);
                Grid.SetRow(b, i);
                Grid.SetColumn(b, 1);
                g.Children.Add(a);
                g.Children.Add(b);
            }
            return g;
        }

        // Pastilla de estado con su punto (late si está esperando)
        static Border Chip(string texto, string clave, bool late = false)
        {
            var b = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            Pincel(b, Border.BackgroundProperty, clave == "Aviso" ? "AvisoFondo" : "Tarjeta");
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            var p = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            Pincel(p, Shape.FillProperty, clave);
            if (late && animar)
                p.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, new Duration(TimeSpan.FromMilliseconds(900)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
            s.Children.Add(p);
            s.Children.Add(BloqueTexto(texto, 13, "SemiBold", clave == "Aviso" ? "Aviso" : "Texto", "0"));
            b.Child = s;
            return b;
        }

        // Fases de un trabajo, en una fila de puntos con nombre
        static readonly Dictionary<string, string> IconoFase = new Dictionary<string, string> { { "espera", "○" }, { "en_curso", "●" }, { "hecho", "✓" }, { "fallo", "✗" } };
        static readonly Dictionary<string, string> ColorFase = new Dictionary<string, string> { { "espera", "Texto2" }, { "en_curso", "Acento" }, { "hecho", "Ok" }, { "fallo", "Mal" } };
        static readonly Dictionary<string, string> NombreFase = new Dictionary<string, string> { { "espera", "pendiente" }, { "en_curso", "en curso" }, { "hecho", "hecho" }, { "fallo", "ha fallado" } };

        static void PintarFases(Panel panel, List<Fase> pasos)
        {
            panel.Children.Clear();
            foreach (var p in pasos)
            {
                string estado = p.Estado;
                var t = new TextBlock { FontSize = Tam(12), Margin = new Thickness(0, 0, 14, 4) };
                var r1 = new Run(IconoFase[estado] + " ") { FontWeight = FontWeights.Bold };
                Pincel(r1, FG, ColorFase[estado]);
                var r2 = new Run(p.Texto);
                Pincel(r2, FG, estado == "espera" ? "Texto2" : "Texto");
                t.Inlines.Add(r1);
                t.Inlines.Add(r2);
                AutomationProperties.SetName(t, p.Texto + ": " + NombreFase[estado]);
                panel.Children.Add(t);
            }
        }

        Button Enlace(string texto, string accion, HorizontalAlignment donde, string margen)
        {
            var b = new Button { Style = (Style)Ventana.FindResource("Enlace"), Content = texto, Tag = accion, HorizontalAlignment = donde, Margin = Margen(margen) };
            b.Click += delegate { Accion(accion); };
            return b;
        }

        // Imagen de la guía: todas van dentro del .exe (se ven sin conexión). «alt» es lo que lee un lector de pantalla
        static FrameworkElement BloqueImagen(string archivo, string pie, string alt)
        {
            var s = new StackPanel { Margin = new Thickness(0, 2, 0, 12) };
            var img = new Image { Stretch = Stretch.Uniform, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            var flujo = Recurso("guia." + archivo);
            if (flujo == null) throw new InvalidOperationException("Falta la imagen de la guía " + archivo + " dentro del instalador.");
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = flujo;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            img.Source = bmp;
            AutomationProperties.SetName(img, alt ?? pie ?? "");
            s.Children.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = img, HorizontalAlignment = HorizontalAlignment.Left });
            if (pie != null) s.Children.Add(BloqueTexto(pie, 12, "Normal", "Texto2", "0,4,0,0"));
            return s;
        }

        static Border BloqueAviso(bool peligro, string texto)
        {
            var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 2, 0, 12), BorderThickness = new Thickness(3, 0, 0, 0) };
            Pincel(b, Border.BorderBrushProperty, peligro ? "Aviso" : "Acento");
            Pincel(b, Border.BackgroundProperty, peligro ? "AvisoFondo" : "AcentoSuave");
            b.Child = BloqueTexto(texto, 13, "Normal", "Texto", "0");
            return b;
        }

        static StackPanel BloquePasos(int desde, params string[] lista)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            int i = desde;
            foreach (string l in lista)
            {
                var d = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                var n = Numero(i++, "0,0,10,0");
                DockPanel.SetDock(n, Dock.Left);
                d.Children.Add(n);
                d.Children.Add(BloqueTexto(l, 13, "Normal", "Texto", "0,2,0,0"));
                s.Children.Add(d);
            }
            return s;
        }

        static Grid BloqueTabla(params string[][] filas)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            int ncol = filas[0].Length;
            for (int j = 0; j < ncol; j++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(j == 0 ? 2 : 3, GridUnitType.Star) });
            for (int i = 0; i < filas.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition());
                for (int j = 0; j < ncol; j++)
                {
                    var celda = new Border { Padding = new Thickness(8, 6, 8, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
                    Pincel(celda, Border.BorderBrushProperty, "Borde");
                    if (i == 0) Pincel(celda, Border.BackgroundProperty, "Campo");
                    celda.Child = BloqueTexto(filas[i][j], 12, i == 0 ? "SemiBold" : "Normal", i == 0 ? "Texto2" : "Texto", "0");
                    Grid.SetRow(celda, i);
                    Grid.SetColumn(celda, j);
                    g.Children.Add(celda);
                }
            }
            return g;
        }

        static Border BloqueCodigo(string texto)
        {
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12) };
            Pincel(b, Border.BackgroundProperty, "Campo");
            var t = new TextBox
            {
                Text = texto, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontFamily = new FontFamily("Consolas"),
                FontSize = Tam(12), TextWrapping = TextWrapping.Wrap
            };
            Pincel(t, Control.ForegroundProperty, "Texto");
            b.Child = t;
            return b;
        }

        Button BloqueBoton(string texto, string accion) { return Enlace(texto + "  →", accion, HorizontalAlignment.Left, "0,0,0,12"); }
    }
}
