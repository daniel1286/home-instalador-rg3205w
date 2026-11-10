// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HomeInstalador
{
    // Lo que hacen los botones
    partial class Instalador
    {
        void Accion(string a)
        {
            if (string.IsNullOrEmpty(a)) return;
            if (a.StartsWith("guia:")) { AbrirGuia(int.Parse(a.Substring(5))); return; }
            if (a.StartsWith("web:")) { Abrir(a.Substring(4)); return; }
            switch (a)
            {
                case "descargar": Descargar(null); break;
                case "parar": e.Cancelar = true; break;
                case "reintentar": e.GitHub = "buscando"; Trabajos.Iniciar(trabajos.GitHub); break;
                case "zip": ElegirZip(); break;
                case "instalar": Instalar(); break;
                case "terminar": Ventana.Close(); break;
                case "otra_vez": mostrarFin = false; e.Fase = ""; break;
                case "detalles":
                    bool ver = CajaDetalles.Visibility != Visibility.Visible;
                    Visible(CajaDetalles, ver);
                    Visible(Contenido, !ver);
                    BotonSecundario.Content = ver ? "Ocultar detalles" : "Ver detalles";
                    break;
                case "driver": Driver(); break;
                case "administrador": Abrir("devmgmt.msc"); break;
                case "desbloquear": Desbloquear(); break;
            }
        }

        static void Abrir(string que)
        {
            try { Process.Start(new ProcessStartInfo(que) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        void Empezar(string trabajo, System.Threading.ThreadStart codigo)
        {
            e.Empezar(trabajo);
            trabajoVisto = trabajo;
            Trabajos.Iniciar(codigo);
        }

        void Descargar(string zipLocal)
        {
            if (e.Trabajo != "") return;
            var v = version;
            bool faltanHerr = string.IsNullOrEmpty(e.FB);
            bool conHome = zipLocal != null || (v != null && v.Carpeta == null && v.Remoto != null);
            if (!faltanHerr && !conHome) return;
            if (conHome && zipLocal == null)
            {
                long hace = v.Remoto.Tamano * 3, libre;
                try { libre = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(descargas))).AvailableFreeSpace; }
                catch (Exception) { libre = long.MaxValue; }
                if (libre < hace)
                {
                    Aviso(string.Format("Hace falta unos {0} libres en el disco de la carpeta de descargas y hay {1}.\n\nLibera espacio o elige otra carpeta en «Opciones».",
                        Imagenes.TamanoTexto(hace), Imagenes.TamanoTexto(libre)), "Instalar Home");
                    return;
                }
            }
            string numero = zipLocal != null ? Regex.Replace(System.IO.Path.GetFileName(zipLocal), @"^Home-(.+)-RG3205W\.zip$", "$1") : v.Numero;
            var remoto = e.Releases.FirstOrDefault(r => r.Numero == numero) ?? new Release();
            e.NuevasFases("herramientas", "descargar", "comprobar zip", "descomprimir", "comprobar imágenes");
            NuevoRegistro("descarga_" + numero);
            Registrar("== Descarga · " + Ahora());
            if (faltanHerr) Registrar("   Herramientas de Google: " + Rutas.UrlHerramientas);
            if (conHome) Registrar("   Home " + numero + (zipLocal != null ? " desde " + zipLocal : ": " + remoto.Url));
            var plan = new PlanDescarga
            {
                Herramientas = faltanHerr, Home = conHome, Numero = numero, Carpeta = descargas, ZipLocal = zipLocal,
                Url = remoto.Url, Zip = remoto.Zip, Sums = remoto.Sums, Huella = remoto.Huella
            };
            Empezar("descargar", () => trabajos.Descargar(plan));
        }

        void AbrirGuia(int capitulo)
        {
            Visible(CapaOpciones, false);
            Visible(CapaGuia, true);
            Animar(CapaGuia, UIElement.OpacityProperty, 0, 1, 250);
            if (capitulo >= 0) ((RadioButton)IndiceGuia.Children[capitulo]).IsChecked = true;
        }

        void ElegirZip()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Home para RG3205W (Home-*-RG3205W.zip)|Home-*-RG3205W.zip" };
            if (d.ShowDialog(Ventana) == true && e.Trabajo == "") Descargar(d.FileName);
        }

        void Instalar()
        {
            var v = version;
            var a = e.Aparato;
            if (v == null || v.Carpeta == null || a.Estado != "ok" || e.Trabajo != "" || demo != null) return;
            string msg = "Se va a instalar Home " + v.Numero + " en la Askey " + a.Serie + ".\n\nSe BORRAN todos los datos del aparato.\n\n¿Seguir?";
            if (MessageBox.Show(Ventana, msg, "Instalar Home", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            e.NuevasFases("SHA256", "boot", "recovery", "vendor", "system", "splash", "cache", "userdata");
            NuevoRegistro("Home_" + v.Numero);
            Registrar("== Home " + v.Numero + " · Askey " + a.Serie + " · " + Ahora());
            Registrar("   Carpeta: " + v.Carpeta);
            Registrar("   fastboot: " + e.FB);
            instalando = v;
            string carpeta = v.Carpeta, serie = a.Serie;
            Empezar("instalar", () => trabajos.Instalar(carpeta, serie));
        }

        void Driver()
        {
            if (e.Trabajo != "" || demo != null) return;
            NuevoRegistro("driver_usb");
            Registrar("== Driver USB de Google · " + Ahora());
            e.Pasos = null;
            Empezar("driver", trabajos.Driver);
        }

        void Desbloquear()
        {
            var a = e.Aparato;
            if (a.Estado != "bloqueada" || e.Trabajo != "" || demo != null) return;
            string msg = "Se va a desbloquear el bootloader de la Askey " + a.Serie + ".\n\nSi en su pantalla sale una pregunta, acéptala con los botones de volumen y encendido.\n\n" +
                "No vuelvas a bloquearlo nunca con Home instalado: dejaría de arrancar.\n\n¿Seguir?";
            if (MessageBox.Show(Ventana, msg, "Desbloquear", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            NuevoRegistro("desbloqueo");
            Registrar("== Desbloqueo · Askey " + a.Serie + " · " + Ahora());
            e.Pasos = null;
            Empezar("desbloquear", trabajos.Desbloquear);
        }

        // «Opciones»: lo poco frecuente, en una pantalla aparte con una fila por cosa
        Border FilaOpcion(string titulo, string texto, string[][] botones, bool elegida = false)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 14, 20, 14), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1.5) };
            Pincel(b, Border.BackgroundProperty, elegida ? "AcentoSuave" : "Tarjeta");
            Pincel(b, Border.BorderBrushProperty, elegida ? "Acento" : "Tarjeta");
            var d = new DockPanel();
            var acc = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(acc, Dock.Right);
            foreach (var par in botones)
            {
                string que = par[1];
                var bt = new Button { Style = (Style)Ventana.FindResource("Enlace"), Content = par[0], Margin = new Thickness(18, 0, 0, 0), IsEnabled = e.Trabajo == "" };
                bt.Click += delegate { Opcion(que); };
                acc.Children.Add(bt);
            }
            var s = new StackPanel();
            s.Children.Add(BloqueTexto(titulo, 15, "SemiBold", "Texto", "0"));
            if (texto != null) s.Children.Add(BloqueTexto(texto, 12, "Normal", "Texto2", "0,3,0,0"));
            d.Children.Add(acc);
            d.Children.Add(s);
            b.Child = d;
            return b;
        }

        void PintarOpciones()
        {
            var p = ContenidoOpciones;
            p.Children.Clear();
            p.Children.Add(BloqueTexto("Versión de Home", 12, "SemiBold", "Texto2", "4,0,0,8"));
            if (catalogo.Count == 0)
                p.Children.Add(FilaOpcion("Sin versiones", "No hay conexión con GitHub ni nada descargado.", new[] { new[] { "Reintentar", "reintentar" } }));
            foreach (var v in catalogo)
            {
                bool elegida = version != null && v.Numero == version.Numero;
                var bot = elegida ? new[] { new[] { "En uso", "" } } : new[] { new[] { "Elegir", "version:" + v.Numero } };
                p.Children.Add(FilaOpcion("Home " + v.Numero, DatosVersion(v, true), bot, elegida));
            }
            p.Children.Add(BloqueTexto("Archivos", 12, "SemiBold", "Texto2", "4,14,0,8"));
            p.Children.Add(FilaOpcion("Ya tengo el zip de Home", "Usa un Home-…-RG3205W.zip que hayas bajado antes, sin volver a descargarlo.", new[] { new[] { "Elegir zip…", "zip" } }));
            p.Children.Add(FilaOpcion("Carpeta de descargas", Rutas.SinUsuario(descargas), new[] { new[] { "Cambiar", "cambiar" }, new[] { "Abrir", "abrir" } }));
            p.Children.Add(FilaOpcion("Registros", "Lo que se ha hecho en cada descarga e instalación. Útil si algo falla.", new[] { new[] { "Abrir", "registros" } }));
            p.Children.Add(FilaOpcion("Todas las versiones", "Las releases de Home en GitHub, con sus notas y archivos.", new[] { new[] { "Abrir ↗", "releases" } }));
        }

        void Opcion(string que)
        {
            if (que.StartsWith("version:"))
            {
                string n = que.Substring(8);
                ElegirVersion(catalogo.FirstOrDefault(x => x.Numero == n));
            }
            else switch (que)
            {
                case "reintentar": Accion("reintentar"); break;
                case "zip": Visible(CapaOpciones, false); ElegirZip(); break;
                case "cambiar":
                    using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Carpeta donde se guarda Home" })
                    {
                        if (Directory.Exists(descargas)) d.SelectedPath = descargas;
                        if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            descargas = d.SelectedPath;
                            Rutas.GuardarDescargas(descargas);
                            RellenarVersiones();
                        }
                    }
                    break;
                case "abrir": AbrirCarpeta(descargas); break;
                case "registros": AbrirCarpeta(Rutas.Registros); break;
                case "releases": Abrir(Rutas.Web + "/releases"); break;
            }
            if (CapaOpciones.Visibility == Visibility.Visible) PintarOpciones();
        }

        static void AbrirCarpeta(string carpeta)
        {
            try { Directory.CreateDirectory(carpeta); } catch (Exception) { }
            Abrir(carpeta);
        }
    }
}
