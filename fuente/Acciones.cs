// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
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
                case "descargar": Descargar(null, false); break;
                case "herramientas": mostrarFin = false; e.Fase = ""; Descargar(null, true); break;
                case "parar": e.Cancelar = true; break;
                case "cancelar": e.Cancelar = true; break;
                case "detener":
                    Preguntar("¿Detener la instalación?",
                        "Se termina de grabar la imagen que está en marcha y no se sigue. La Askey se queda **sin un sistema completo** hasta que vuelvas a instalar Home.",
                        "Detener", "Seguir instalando", true, delegate { e.Cancelar = true; });
                    break;
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
                    AjustarDetalles();
                    break;
                case "driver": Driver(); break;
                case "administrador": Abrir("devmgmt.msc"); break;
                case "desbloquear": Desbloquear(); break;
                case "informe": AbrirInforme(); break;
            }
        }

        void Abrir(string que)
        {
            try { Process.Start(new ProcessStartInfo(que) { UseShellExecute = true }); }
            catch (Win32Exception ex) { Avisar("No se ha podido abrir", "Windows no ha podido abrir «" + Rutas.SinUsuario(que) + "»: " + ex.Message); }
            catch (FileNotFoundException ex) { Avisar("No se ha podido abrir", ex.Message); }
        }

        // Un trabajo en su hilo. Mientras dura, el PC no se suspende: cortaría la descarga o el USB a mitad
        void Empezar(string trabajo, System.Threading.ThreadStart codigo)
        {
            e.Empezar(trabajo);
            trabajoVisto = trabajo;
            inicioTrabajo = DateTime.UtcNow;
            serieTrabajo = e.Aparato != null ? e.Aparato.Serie : "";
            algoHecho = true;
            Trabajos.Iniciar(delegate
            {
                Sistema.SinSuspender(true);
                try { codigo(); }
                finally { Sistema.SinSuspender(false); }
            });
        }

        // ------------------------------------------------------------ informe de un fallo

        string serieTrabajo = "", informeHome, informePaso, informeTexto, informeCodigo;

        // Al fallar un trabajo se prepara su informe ya limpio; solo se envía si la persona lo pide en su pantalla
        void PrepararInforme(bool bien)
        {
            informeTexto = null;
            informeCodigo = null;
            if (bien || e.Motivo == "cancelado" || trabajoVisto == "") return;
            var v = trabajoVisto == "instalar" ? instalando : version;
            string home = NumeroPublicado(v);
            if (home == null) return;
            informeHome = home;
            informePaso = Estadistica.Paso(trabajoVisto, e.Pasos, e.Motivo);
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
                Text = informeTexto.Replace("\n", "\r\n"), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = Tam(12),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            AutomationProperties.SetName(texto, "Texto del informe");
            Pincel(texto, FG, "Texto2");
            caja.Child = texto;
            p.Children.Add(caja);

            if (informeCodigo != null)
            {
                var hecho = Tarjeta(Resumen(new[] { new[] { "Enviado", "Código " + informeCodigo, "Ok" } }));
                hecho.Margin = new Thickness(0, 14, 0, 0);
                p.Children.Add(hecho);
                p.Children.Add(BloqueTexto("Si abres una incidencia en GitHub, pon este código: así se ve tu registro sin que tengas que adjuntarlo.", 13, "Normal", "Texto2", "0,10,0,6"));
                var fila = new WrapPanel();
                var copiar = new Button { Style = (Style)Ventana.FindResource("Enlace"), Content = "Copiar el código", Margin = new Thickness(0, 0, 22, 0) };
                copiar.Click += delegate
                {
                    try { Clipboard.SetText(informeCodigo); copiar.Content = "Copiado ✓"; }
                    catch (System.Runtime.InteropServices.COMException) { Avisar("No se ha podido copiar", "Otro programa está usando el portapapeles. Vuelve a probar en un momento."); }
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
                string codigo, fallo = "";
                if (prueba) { System.Threading.Thread.Sleep(700); codigo = "H-7K3Q2M"; }
                else codigo = Estadistica.EnviarInforme(mensaje, out fallo);
                Ventana.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (codigo != null)
                    {
                        informeCodigo = codigo;
                        if (!prueba) Registrar("Informe del fallo enviado: " + codigo);
                    }
                    else Registrar("No se ha podido enviar el informe: " + fallo);
                    PintarInforme(false);
                    if (codigo == null)
                        Avisar("No se ha podido enviar", fallo + "\n\nPrueba más tarde o, si abres una incidencia en GitHub, adjunta el registro de «Opciones» → «Registros».");
                }));
            });
        }

        DateTime inicioTrabajo;

        // «Contar esta instalación»: la instalación al acabar, bien o mal, y los fallos de los pasos de antes. Lo que para
        // la persona (detener, cancelar) no es un fallo
        void Contar(bool bien)
        {
            if (demo != null || CasillaEstadistica.IsChecked != true) return;
            string t = trabajoVisto;
            if (t == "" || (bien && t != "instalar")) return;
            if (!bien && e.Motivo == "cancelado") return;
            string home = NumeroPublicado(t == "instalar" ? instalando : version);
            if (home == null) { Registrar("No se cuenta en las estadísticas: esta versión de Home no está en la lista de versiones publicadas."); return; }
            long segundos = (long)(DateTime.UtcNow - inicioTrabajo).TotalSeconds;
            Estadistica.Enviar(Estadistica.Mensaje(home, bien ? "ok" : "fallo", Estadistica.Paso(t, e.Pasos, e.Motivo), segundos, e.Motivo), e);
        }

        // «Qué se envía»: lo mismo que el texto exacto, contado en una tabla, y el texto exacto si se quiere ver
        void QueSeEnvia()
        {
            string home = version != null ? version.Numero : "2.1.1";
            var s = new StackPanel();
            s.Children.Add(BloqueTexto("Con la casilla marcada, al terminar se envía a las estadísticas de Home si la instalación ha salido bien o en qué paso ha fallado. Solo esto:", 14, "Normal", "Texto2", "0,0,0,10"));
            var filas = new List<string[]>
            {
                new[] { "Cómo", "por cable, con este instalador (" + Estadistica.Instalador + ")" },
                new[] { "Home", "la versión que instalas (" + home + ")" },
                new[] { "Resultado", "bien, o el paso en el que ha fallado (por ejemplo, «grabar system»)" },
                new[] { "Tiempo", "cuánto ha tardado, en segundos" }
            };
            if (Estadistica.Acepta.Contains("motivo")) filas.Add(new[] { "Causa", "si falla, el tipo de fallo (por ejemplo, «USB cortado»)" });
            if (Estadistica.Acepta.Contains("abandono")) filas.Add(new[] { "Al cerrar", "si cierras sin instalar, hasta qué paso llegaste" });
            s.Children.Add(Tarjeta(Resumen(filas.ToArray())));
            s.Children.Add(BloqueTexto("La descarga de Home pasa por el servidor de Home camino de GitHub y, con la casilla marcada, la cuenta: solo el día, la versión y que viene del instalador.", 13, "Normal", "Texto2", "0,12,0,6"));
            s.Children.Add(BloqueTexto("**Nada más:** ni identificador, ni nombre, ni datos de este PC o de la Askey, y la dirección IP no se guarda. Puedes desmarcarla cuando quieras; se recuerda en este PC.", 13, "Normal", "Texto2", "0,0,0,6"));
            var exacto = new StackPanel { Visibility = Visibility.Collapsed };
            string ejemplos = Estadistica.Mensaje(home, "ok", null, 840) + "\n" + Estadistica.Mensaje(home, "fallo", "grabar_system", 312, "usb");
            if (Estadistica.Acepta.Contains("abandono")) ejemplos += "\n" + Estadistica.Mensaje(home, "abandono", "sin_driver", 1260);
            exacto.Children.Add(BloqueCodigo(ejemplos));
            var ver = new Button { Style = (Style)Ventana.FindResource("Enlace"), Content = "Ver el texto exacto", HorizontalAlignment = HorizontalAlignment.Left };
            ver.Click += delegate
            {
                bool si = exacto.Visibility != Visibility.Visible;
                Visible(exacto, si);
                ver.Content = si ? "Ocultar el texto exacto" : "Ver el texto exacto";
            };
            s.Children.Add(ver);
            s.Children.Add(exacto);
            Mostrar(new Dialogo { Titulo = "Qué se envía", Cuerpo = s, Botones = new[] { "Entendido" }, Principal = 0, Cancelar = 0 });
        }

        // ------------------------------------------------------------ descargar

        // soloHerramientas: volver a bajar fastboot aunque haya Home
        void Descargar(string zipLocal, bool soloHerramientas)
        {
            if (e.Trabajo != "" || demo != null) return;
            var v = version;
            bool faltanHerr = string.IsNullOrEmpty(e.FB) || soloHerramientas;
            bool conHome = !soloHerramientas && (zipLocal != null || (v != null && v.Carpeta == null && v.Remoto != null));
            if (!faltanHerr && !conHome) return;

            string numero, raiz = null;
            if (zipLocal != null)
            {
                // La versión sale de dentro del zip, no de su nombre
                try { raiz = Imagenes.RaizDelZip(zipLocal); }
                catch (InvalidDataException) { raiz = null; }
                catch (IOException ex) { Avisar("No se puede abrir el zip", ex.Message); return; }
                if (raiz == null) { Avisar("Este zip no es de Home", "No es un paquete de Home para el RG3205W, o está dañado. Elige el zip «Home-…-RG3205W.zip» que se descarga de la página de versiones."); return; }
                numero = Imagenes.NumeroDeCarpeta(raiz);
                // Al acabar, la versión de ese zip queda elegida
                elegidaAMano = numero;
            }
            else numero = v != null ? v.Numero : "";
            var remoto = e.Releases.FirstOrDefault(r => r.Numero == numero) ?? new Release();

            if (conHome && zipLocal == null)
            {
                // Antes de empezar: el zip y lo que sale de él, unas tres veces el zip
                long hace = v.Remoto.Tamano * 3, libre;
                try { libre = Imagenes.Libre(descargas); }
                catch (IOException) { libre = long.MaxValue; }
                catch (ArgumentException) { libre = long.MaxValue; }
                if (libre < hace)
                {
                    Avisar("Falta espacio", Imagenes.FaltaEspacio(descargas, hace, libre).Message, "Cambiar carpeta", () => Opcion("cambiar"));
                    return;
                }
            }

            e.NuevasFases(conHome ? new[] { "herramientas", "descargar", "zip", "descomprimir", "imagenes" }.Where(f => faltanHerr || f != "herramientas").ToArray() : new[] { "herramientas" });
            NuevoRegistro(conHome ? "descarga_" + numero : "herramientas");
            Registrar("== Descarga · " + Ahora());
            if (faltanHerr) Registrar("   Herramientas de Google: " + Rutas.UrlHerramientas);
            if (conHome) Registrar("   Home " + numero + (zipLocal != null ? " desde " + zipLocal : ": " + remoto.Url));
            if (conHome) Registrar("   Carpeta: " + descargas);
            var plan = new PlanDescarga
            {
                Herramientas = faltanHerr, Home = conHome, Numero = numero, Carpeta = descargas, ZipLocal = zipLocal,
                Url = Red.Contada(remoto.Url, CasillaEstadistica.IsChecked == true), Zip = zipLocal != null ? raiz + ".zip" : remoto.Zip,
                Sums = remoto.Sums, Huella = remoto.Huella, Tamano = remoto.Tamano
            };
            Empezar("descargar", () => trabajos.Descargar(plan));
        }

        void AbrirGuia(int capitulo)
        {
            Visible(CapaOpciones, false);
            Visible(CapaGuia, true);
            Animar(CapaGuia, UIElement.OpacityProperty, 0, 1, 250);
            var elegido = (RadioButton)IndiceGuia.Children[Math.Max(0, capitulo)];
            // Si ya estaba elegido se vuelve a pintar: los tamaños y versiones pueden haber llegado después
            if (capitulo < 0) PintarCapitulo(IndiceGuia.Children.IndexOf(IndiceGuia.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) ?? elegido));
            else if (elegido.IsChecked == true) PintarCapitulo(capitulo);
            else elegido.IsChecked = true;
        }

        void ElegirZip()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Paquete de Home (*.zip)|*.zip", Title = "Elige el zip de Home" };
            if (d.ShowDialog(Ventana) == true && e.Trabajo == "") Descargar(d.FileName, false);
        }

        // Otros fastboot o adb abiertos pueden quedarse con la Askey a mitad: se nombran y se pueden cerrar desde aquí
        void SinOtrosFastboot(Action seguir)
        {
            var otros = Usb.OtrosFastboot(Fastboot.Propios());
            if (otros.Count == 0) { seguir(); return; }
            var lista = otros.Select(o => o.Nombre + (string.IsNullOrEmpty(o.Ruta) ? "" : " · " + Rutas.SinUsuario(o.Ruta))).Distinct().ToList();
            var extra = Tarjeta(ListaPuntos(lista, false));
            Preguntar("Hay otros programas usando fastboot o adb",
                "Pueden quitarle la Askey al instalador a mitad de trabajo. Ciérralos (otra ventana de comandos, Android Studio, un programa para móviles…) o deja que el instalador los cierre.",
                "Cerrarlos y seguir", "Cancelar", false, delegate
                {
                    var quedan = new List<string>();
                    foreach (var o in otros)
                    {
                        try { using (var p = Process.GetProcessById(o.Id)) { p.Kill(); p.WaitForExit(3000); } }
                        catch (ArgumentException) { }
                        catch (InvalidOperationException) { }
                        catch (Win32Exception) { quedan.Add(o.Nombre + (string.IsNullOrEmpty(o.Ruta) ? "" : " (" + Rutas.SinUsuario(o.Ruta) + ")")); }
                    }
                    foreach (var o in otros) Registrar("Cerrado otro " + o.Nombre + (string.IsNullOrEmpty(o.Ruta) ? "" : ": " + Rutas.SinUsuario(o.Ruta)));
                    if (quedan.Count > 0)
                        Avisar("No se han podido cerrar", "Windows no deja cerrarlos desde aquí (los ha abierto otra cuenta o un administrador): " + string.Join(", ", quedan) + ". Ciérralos tú y vuelve a pulsar el botón.");
                    else seguir();
                }, extra);
        }

        void Instalar()
        {
            var v = version;
            var a = e.Aparato;
            if (v == null || v.Carpeta == null || a.Estado != "ok" || e.Trabajo != "" || demo != null) return;
            SinOtrosFastboot(delegate
            {
                Preguntar("¿Instalar Home " + v.Numero + "?",
                    "Se va a instalar Home " + v.Numero + " en la Askey " + a.Serie + ".\n\n**Se borran todos los datos del aparato.**",
                    "Instalar y borrar", "Cancelar", true, delegate
                    {
                        if (e.Trabajo != "" || e.Aparato.Serie != a.Serie || e.Aparato.Estado != "ok") return;
                        e.NuevasFases(new[] { "imagenes", "askey" }.Concat(ListaParticiones(v.Carpeta)).Concat(new[] { "cache", "userdata" }).ToArray());
                        NuevoRegistro("Home_" + v.Numero);
                        Registrar("== Home " + v.Numero + " · Askey " + a.Serie + " · " + Ahora());
                        Registrar("   Carpeta: " + v.Carpeta);
                        Registrar("   fastboot: " + e.FB);
                        instalando = v;
                        instalacionIntentada = true;
                        string carpeta = v.Carpeta, serie = a.Serie;
                        Empezar("instalar", () => trabajos.Instalar(carpeta, serie));
                    });
            });
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
            SinOtrosFastboot(delegate
            {
                Preguntar("¿Desbloquear el bootloader?",
                    "Se va a desbloquear el bootloader de la Askey " + a.Serie + ". Si en su pantalla sale una pregunta, acéptala con los botones de volumen y encendido.\n\n" +
                    "**No vuelvas a bloquearlo nunca con Home instalado:** dejaría de arrancar.",
                    "Desbloquear", "Cancelar", false, delegate
                    {
                        if (e.Trabajo != "" || e.Aparato.Serie != a.Serie) return;
                        NuevoRegistro("desbloqueo");
                        Registrar("== Desbloqueo · Askey " + a.Serie + " · " + Ahora());
                        e.Pasos = null;
                        string serie = a.Serie;
                        Empezar("desbloquear", () => trabajos.Desbloquear(serie));
                    });
            });
        }

        // ------------------------------------------------------------ opciones

        // «Opciones»: lo poco frecuente, en una pantalla aparte con una fila por cosa
        Border FilaOpcion(string titulo, string texto, string[][] botones, bool elegida = false)
        {
            var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 14, 20, 14), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(1.5) };
            Pincel(b, Border.BackgroundProperty, elegida ? "AcentoSuave" : "Tarjeta");
            Pincel(b, Border.BorderBrushProperty, elegida ? "Acento" : "Tarjeta");
            var d = new DockPanel();
            var acc = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(acc, Dock.Right);
            foreach (var par in botones)
            {
                string que = par[1];
                var bt = new Button { Style = (Style)Ventana.FindResource("Enlace"), Content = par[0], Margin = new Thickness(18, 0, 0, 0), IsEnabled = e.Trabajo == "" && que != "" };
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
                p.Children.Add(FilaOpcion("Sin versiones", string.IsNullOrEmpty(e.ErrorGitHub) ? "Sin conexión y sin nada descargado." : e.ErrorGitHub, new[] { new[] { "Reintentar", "reintentar" } }));
            foreach (var v in catalogo)
            {
                bool elegida = version != null && v.Numero == version.Numero;
                var bot = elegida ? new[] { new[] { "En uso", "" } } : new[] { new[] { "Elegir", "version:" + v.Numero } };
                p.Children.Add(FilaOpcion("Home " + v.Numero, DatosVersion(v, true), bot, elegida));
            }
            p.Children.Add(BloqueTexto("Archivos", 12, "SemiBold", "Texto2", "4,14,0,8"));
            p.Children.Add(FilaOpcion("Ya tengo el zip de Home", "Usa un zip de Home que hayas bajado antes, sin volver a descargarlo.", new[] { new[] { "Elegir zip…", "zip" } }));
            p.Children.Add(FilaOpcion("Carpeta de descargas", Rutas.SinUsuario(descargas) + (Sistema.EnOneDrive(descargas) ? " · en OneDrive" : ""), new[] { new[] { "Cambiar", "cambiar" }, new[] { "Abrir", "abrir" } }));
            p.Children.Add(FilaOpcion("Herramientas de Google", e.Herr == "ok" ? "fastboot listo en este PC." : HerramientasTexto() ?? "Todavía no están en este PC.", new[] { new[] { "Volver a bajar", "herramientas" } }));
            p.Children.Add(FilaOpcion("Registros", "Lo que se ha hecho en cada descarga e instalación. Útil si algo falla.", new[] { new[] { "Abrir", "registros" } }));
            p.Children.Add(FilaOpcion("Todas las versiones", "Las versiones de Home en GitHub, con sus notas y archivos.", new[] { new[] { "Abrir ↗", "releases" } }));
        }

        void Opcion(string que)
        {
            if (que.StartsWith("version:"))
            {
                string n = que.Substring(8);
                elegidaAMano = n;
                ElegirVersion(catalogo.FirstOrDefault(x => x.Numero == n));
            }
            else switch (que)
            {
                case "reintentar": Accion("reintentar"); break;
                case "zip": Visible(CapaOpciones, false); ElegirZip(); break;
                case "herramientas": Visible(CapaOpciones, false); Accion("herramientas"); break;
                case "cambiar":
                    using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Carpeta donde se guarda Home (hacen falta unos 4 GB libres)" })
                    {
                        if (Directory.Exists(descargas)) d.SelectedPath = descargas;
                        if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        {
                            string elegida = d.SelectedPath;
                            if (Sistema.EnOneDrive(elegida))
                                Preguntar("Esa carpeta está en OneDrive",
                                    "OneDrive subiría a la nube varios GB de imágenes y puede bloquearlas mientras se graban. Es mejor una carpeta fuera de OneDrive.",
                                    "Usarla igualmente", "Elegir otra", false, () => CambiarCarpeta(elegida));
                            else CambiarCarpeta(elegida);
                        }
                    }
                    break;
                case "abrir": AbrirCarpeta(descargas); break;
                case "registros": AbrirCarpeta(Rutas.Registros); break;
                case "releases": Abrir(Rutas.Web + "/releases"); break;
            }
            if (CapaOpciones.Visibility == Visibility.Visible) PintarOpciones();
        }

        void CambiarCarpeta(string carpeta)
        {
            descargas = carpeta;
            if (!Rutas.GuardarDescargas(descargas)) Avisar("No se ha podido guardar", "Se usará esa carpeta ahora, pero la próxima vez el instalador volverá a la de siempre.");
            RellenarVersiones();
            if (CapaOpciones.Visibility == Visibility.Visible) PintarOpciones();
        }

        void AbrirCarpeta(string carpeta)
        {
            try { Directory.CreateDirectory(carpeta); }
            catch (IOException ex) { Avisar("No se ha podido abrir la carpeta", ex.Message); return; }
            catch (UnauthorizedAccessException ex) { Avisar("No se ha podido abrir la carpeta", ex.Message); return; }
            Abrir(carpeta);
        }
    }
}
