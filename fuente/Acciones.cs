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
                case "reintentar": e.GitHub = "buscando"; Trabajos.Iniciar(trabajos.BuscarVersiones); break;
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
                case "informe": AbrirInforme(); break;
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
            inicioTrabajo = DateTime.UtcNow;
            serieTrabajo = e.Aparato != null ? e.Aparato.Serie : "";
            Trabajos.Iniciar(codigo);
        }

        // ------------------------------------------------------------ informe de un fallo

        string serieTrabajo = "", informeHome, informePaso, informeTexto, informeCodigo;

        // Al fallar un trabajo se prepara su informe ya limpio; solo se envía si la persona lo pide en su pantalla
        void PrepararInforme(bool bien)
        {
            informeTexto = null;
            informeCodigo = null;
            if (bien || e.Error == Red.Detenida || e.Error == Trabajos.Cancelado || trabajoVisto == "") return;
            var v = trabajoVisto == "instalar" ? instalando : version;
            if (v == null) return;
            informeHome = v.Numero;
            informePaso = Estadistica.Paso(trabajoVisto, e.Pasos);
            informeTexto = Estadistica.Limpiar(TextoRegistro.Text, serieTrabajo, e.Aparato != null ? e.Aparato.Serie : "");
        }

        void AbrirInforme()
        {
            if (informeTexto == null) return;
            Visible(CapaGuia, false);
            Visible(CapaOpciones, false);
            PintarInforme(false);
            Visible(CapaInforme, true);
            Animar(CapaInforme, UIElement.OpacityProperty, 0, 1, 250);
        }

        void PintarInforme(bool enviando)
        {
            var p = ContenidoInforme;
            p.Children.Clear();
            p.Children.Add(BloqueTexto("Si quieres ayudar a arreglar este fallo, se envía a Home el registro de lo que ha hecho el instalador. " +
                "**Este es el texto exacto**, ya limpio: sin el número de serie de la Askey, sin tu usuario ni el nombre del PC y sin direcciones " +
                "de correo, IP o MAC. Se guarda 90 días y solo lo ve quien mantiene Home.", 14, "Normal", "Texto2", "0,0,0,14"));
            p.Children.Add(Tarjeta(Resumen(new[]
            {
                new[] { "Home", informeHome },
                new[] { "Falló en", informePaso },
                new[] { "Instalador", Estadistica.Instalador + " · por cable" }
            })));
            var caja = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 10, 0, 0), Height = 230 };
            Pincel(caja, Border.BackgroundProperty, "Tarjeta");
            var texto = new TextBox
            {
                Text = informeTexto.Replace("\n", "\r\n"), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Pincel(texto, FG, "Texto2");
            caja.Child = texto;
            p.Children.Add(caja);

            if (informeCodigo != null)
            {
                p.Children.Add(Tarjeta(Resumen(new[] { new[] { "Enviado", "Código " + informeCodigo, "Ok" } })));
                ((FrameworkElement)p.Children[p.Children.Count - 1]).Margin = new Thickness(0, 14, 0, 0);
                p.Children.Add(BloqueTexto("Si abres una incidencia en GitHub, pon este código: así se ve tu registro sin que tengas que adjuntarlo.", 13, "Normal", "Texto2", "0,10,0,6"));
                var fila = new StackPanel { Orientation = Orientation.Horizontal };
                var copiar = new Button { Style = (Style)Ventana.FindResource("Enlace"), Content = "Copiar el código", Margin = new Thickness(0, 0, 22, 0) };
                copiar.Click += delegate
                {
                    try { Clipboard.SetText(informeCodigo); copiar.Content = "Copiado ✓"; }
                    catch (Exception) { }
                };
                fila.Children.Add(copiar);
                fila.Children.Add(Enlace("Abrir una incidencia en GitHub →", "web:" + Rutas.Incidencias, HorizontalAlignment.Left, "0"));
                p.Children.Add(fila);
                return;
            }

            var enviar = new Button { Style = (Style)Ventana.FindResource("Principal"), Content = enviando ? "Enviando…" : "Enviar informe",
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0), IsEnabled = !enviando };
            enviar.Click += delegate { EnviarInforme(); };
            p.Children.Add(enviar);
        }

        void EnviarInforme()
        {
            PintarInforme(true);
            string mensaje = Estadistica.Informe(informeHome, informePaso, informeTexto);
            bool prueba = demo != null;
            Trabajos.Iniciar(delegate
            {
                // En demostración no sale nada del PC: se enseña cómo quedaría con un código de ejemplo
                string codigo;
                if (prueba) { System.Threading.Thread.Sleep(700); codigo = "H-7K3Q2M"; }
                else codigo = Estadistica.EnviarInforme(mensaje);
                Ventana.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (codigo != null)
                    {
                        informeCodigo = codigo;
                        if (!prueba) Registrar("Informe del fallo enviado: " + codigo);
                    }
                    PintarInforme(false);
                    if (codigo == null)
                        Aviso("No se ha podido enviar ahora. Prueba más tarde o, si abres una incidencia en GitHub, adjunta el registro de «Opciones» → «Registros».", "Informe del fallo");
                }));
            });
        }

        DateTime inicioTrabajo;

        // «Contar esta instalación»: la instalación al acabar, bien o mal, y los fallos de los pasos de antes. Detener la
        // descarga o cancelar el permiso de administrador no son fallos
        void Contar(bool bien)
        {
            if (demo != null || CasillaEstadistica.IsChecked != true) return;
            string t = trabajoVisto;
            if (t == "" || (bien && t != "instalar")) return;
            if (!bien && (e.Error == Red.Detenida || e.Error == Trabajos.Cancelado)) return;
            var v = t == "instalar" ? instalando : version;
            if (v == null) return;
            long segundos = (long)(DateTime.UtcNow - inicioTrabajo).TotalSeconds;
            Estadistica.Enviar(Estadistica.Mensaje(v.Numero, bien, Estadistica.Paso(t, e.Pasos), segundos), e);
        }

        void QueSeEnvia()
        {
            string home = version != null ? version.Numero : "2.1";
            MessageBox.Show(Ventana,
                "Con «Contar esta instalación» marcada, al terminar se envía a las estadísticas de Home si la instalación ha salido bien " +
                "o en qué paso ha fallado. Exactamente esto:\n\n" +
                Estadistica.Mensaje(home, true, null, 840) + "\n" + Estadistica.Mensaje(home, false, "grabar_system", 312) + "\n\n" +
                "Además, la descarga de Home pasa por el servidor de Home camino de GitHub, y con la casilla marcada la cuenta: solo el " +
                "día, la versión y que viene del instalador.\n\n" +
                "Nada más: ni identificador, ni nombre, ni datos de este PC o de la Askey, y la dirección IP no se guarda. Sirve para saber " +
                "cuántas instalaciones salen bien y en qué paso se atascan.\n\nPuedes desmarcarla cuando quieras; se recuerda en este PC.",
                "Qué se envía", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        void Descargar(string zipLocal)
        {
            if (e.Trabajo != "" || demo != null) return;
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
                Url = Red.Contada(remoto.Url, CasillaEstadistica.IsChecked == true), Zip = remoto.Zip, Sums = remoto.Sums, Huella = remoto.Huella
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
                p.Children.Add(FilaOpcion("Sin versiones", "Sin conexión y sin nada descargado.", new[] { new[] { "Reintentar", "reintentar" } }));
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
