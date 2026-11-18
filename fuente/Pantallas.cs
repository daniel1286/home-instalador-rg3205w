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
using System.Windows.Automation;
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
            // La carpeta elegida y, por si acaso, donde lo dejaban las versiones anteriores del instalador
            var carpetas = new List<string> { descargas };
            foreach (string c in Rutas.DescargasAnteriores())
                if (!carpetas.Any(x => string.Equals(x.TrimEnd('\\'), c.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) carpetas.Add(c);
            foreach (string carpeta in carpetas)
            {
                try
                {
                    if (!Directory.Exists(carpeta)) continue;
                    foreach (var d in new DirectoryInfo(carpeta).GetDirectories("Home-*-RG3205W"))
                    {
                        string num = Imagenes.NumeroDeCarpeta(d.Name);
                        if (num == null || !Imagenes.CarpetaLista(d.FullName)) continue;
                        var ya = v.FirstOrDefault(x => x.Numero == num);
                        if (ya != null) { if (ya.Carpeta == null) ya.Carpeta = d.FullName; }
                        else v.Add(new Version
                        {
                            Numero = num, Fecha = d.LastWriteTime.ToString("dd-MM-yyyy"), Carpeta = d.FullName,
                            Cambios = new List<string> { "Descargada en este PC. Las novedades salen cuando hay conexión." }
                        });
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return v;
        }

        // La versión que eligió la persona en «Opciones»; si no eligió ninguna, la última publicada que no es de prueba
        string elegidaAMano;

        void RellenarVersiones()
        {
            catalogo = LeerCatalogo();
            var v = catalogo.FirstOrDefault(x => x.Numero == elegidaAMano) ?? catalogo.FirstOrDefault(x => !x.Previa && x.Remoto != null)
                ?? catalogo.FirstOrDefault(x => !x.Previa) ?? catalogo.FirstOrDefault();
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
        class PasoRiel { public Border Circulo, Linea; public TextBlock Num, Titulo, Sub; public StackPanel Textos; public string Firma = ""; }
        readonly PasoRiel[] riel = new PasoRiel[5];

        void CrearRiel()
        {
            string[] nombres = { "Descargar", "Conectar", "Desbloquear", "Instalar" };
            double circulo = Math.Max(30, Tam(13) * 2.2);
            for (int i = 1; i <= 4; i++)
            {
                var r = new PasoRiel();
                var fila = new DockPanel();
                var col = new Grid { Width = circulo, Margin = new Thickness(0, 0, 14, 0) };
                DockPanel.SetDock(col, Dock.Left);
                r.Circulo = new Border { Width = circulo, Height = circulo, CornerRadius = new CornerRadius(circulo / 2), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Top };
                r.Num = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = Tam(13), FontWeight = FontWeights.Bold };
                r.Circulo.Child = r.Num;
                r.Linea = new Border { Width = 2, Margin = new Thickness(0, circulo + 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
                Pincel(r.Linea, Border.BackgroundProperty, "Borde");
                if (i < 4) col.Children.Add(r.Linea);
                col.Children.Add(r.Circulo);
                r.Textos = new StackPanel { Margin = new Thickness(0, 4, 0, 24) };
                r.Titulo = new TextBlock { Text = nombres[i - 1], FontSize = Tam(15), FontWeight = FontWeights.SemiBold };
                r.Sub = new TextBlock { FontSize = Tam(12), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
                Pincel(r.Sub, TextBlock.ForegroundProperty, "Texto2");
                r.Textos.Children.Add(r.Titulo);
                r.Textos.Children.Add(r.Sub);
                fila.Children.Add(col);
                fila.Children.Add(r.Textos);
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
            AutomationProperties.SetName(r.Textos, r.Titulo.Text + ", " + (estado == "hecho" ? "hecho" : estado == "actual" ? "paso actual" : "pendiente") + (sub != "" ? ", " + sub : ""));
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
            catch (UnauthorizedAccessException) { ficheroRegistro = null; }
        }

        void NuevoRegistro(string nombre)
        {
            try
            {
                Directory.CreateDirectory(Rutas.Registros);
                ficheroRegistro = System.IO.Path.Combine(Rutas.Registros, DateTime.Now.ToString("yyyy-MM-dd_HHmm") + "_" + nombre + ".txt");
            }
            catch (IOException) { ficheroRegistro = null; }
            catch (UnauthorizedAccessException) { ficheroRegistro = null; }
            TextoRegistro.Clear();
        }

        static string Ahora() { return DateTime.Now.ToString("dd-MM-yyyy HH:mm"); }

        // ------------------------------------------------------------ pantallas

        // Cuándo se empezó a esperar a la Askey: pasado un minuto sin verla, se enseña qué revisar
        DateTime esperandoDesde = DateTime.MaxValue;
        bool EsperaLarga { get { return (DateTime.UtcNow - esperandoDesde).TotalSeconds > 60; } }

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
                case "usb_mal": return "usb";
                case "no_valido": case "varios": case "error": return "otro";
                default: return "conectar";
            }
        }

        void Acciones(string principal, string accionP, string secundario, string accionS)
        {
            BotonPrincipal.Content = principal; BotonPrincipal.Tag = accionP; Visible(BotonPrincipal, principal != null);
            BotonSecundario.Content = secundario; BotonSecundario.Tag = accionS; Visible(BotonSecundario, secundario != null);
        }

        // Por qué faltan las herramientas de Google
        string HerramientasTexto()
        {
            switch (e.Herr)
            {
                case "quitado": return "El antivirus ha quitado o bloqueado fastboot.exe. Vuelve a bajarlo y, si pasa otra vez, añade " + Rutas.SinUsuario(Fastboot.Carpeta) + " a sus exclusiones.";
                case "danado": return "Las herramientas de Google de este PC están dañadas o alguien las ha cambiado. Se vuelven a bajar de Google (son pocos MB).";
                case "antiguo": return "Las herramientas de Google de este PC son muy antiguas. Se bajan las últimas de Google (son pocos MB).";
                default: return null;
            }
        }

        void MostrarPantalla(string k)
        {
            var v = version;
            var a = e.Aparato;
            Contenido.Children.Clear();
            Visible(CajaDetalles, false);
            Visible(Contenido, true);
            panelFases = null;
            firmaFases = "";
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
                        Titulo.Text = "No se encuentran las versiones de Home";
                        Texto.Text = string.IsNullOrEmpty(e.ErrorGitHub) ? "Revisa la conexión a internet." : e.ErrorGitHub;
                        Contenido.Children.Add(Tarjeta(BloqueTexto("Si ya tienes el zip de Home, puedes usarlo sin conexión.", 13, "Normal", "Texto2", "0")));
                        Acciones("Reintentar", "reintentar", "Usar un zip que ya tengo", "zip");
                    }
                    else if (v.Carpeta != null)
                    {
                        PonerHeroe(e.Herr == "falta" ? "espera" : "aviso", "descargar");
                        Titulo.Text = e.Herr == "falta" ? "Faltan las herramientas de Google" : "Hay que volver a bajar fastboot";
                        Texto.Text = HerramientasTexto() ?? "Home " + v.Numero + " ya está descargado. Para grabarlo hace falta fastboot, que se baja de Google (son pocos MB).";
                        Acciones("Descargar fastboot", "descargar", null, null);
                    }
                    else
                    {
                        PonerHeroe("espera", "descargar");
                        Titulo.Text = "Descarga Home " + v.Numero;
                        Texto.Text = Imagenes.TamanoTexto(v.Remoto.Tamano) + " desde " + Red.Sitio(v.Remoto) + (string.IsNullOrEmpty(e.FB) ? ", con fastboot de Google" : "") +
                            ". Se comprueba su huella antes de usarlo.";
                        if (e.Herr != "falta" && e.Herr != "ok" && HerramientasTexto() != null)
                            Contenido.Children.Add(Chip("fastboot: " + (e.Herr == "quitado" ? "lo ha quitado el antivirus" : "se vuelve a bajar"), "Aviso"));
                        var notas = v.Cambios.Take(3).Select(Corto).ToList();
                        if (notas.Count > 0)
                        {
                            var s = new StackPanel();
                            s.Children.Add(BloqueTexto("Qué trae", 12, "SemiBold", "Texto2", "0,0,0,6"));
                            s.Children.Add(ListaPuntos(notas, false));
                            var t = Tarjeta(s);
                            t.Margin = new Thickness(0, 8, 0, 0);
                            Contenido.Children.Add(t);
                        }
                        Acciones("Descargar Home " + v.Numero, "descargar", "¿La Askey tiene el sistema de fábrica? Empieza por la guía →", "guia:0");
                    }
                    break;
                case "descargando":
                    PonerHeroe("progreso", null);
                    Titulo.Text = "Descargando Home " + (v != null ? v.Numero : ""); Texto.Text = "";
                    panelFases = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
                    Contenido.Children.Add(panelFases);
                    Contenido.Children.Add(Tarjeta(BloqueTexto("Mientras baja, puedes ir preparando la Askey: abrirla, conectarla al PC y ponerla en modo fastboot.", 13, "Normal", "Texto2", "0")));
                    Contenido.Children.Add(Enlace("Abrir la guía →", "guia:1", HorizontalAlignment.Center, "0,10,0,0"));
                    Acciones(null, null, "Detener la descarga", "parar");
                    break;
                case "conectar":
                    PonerHeroe("espera", "enchufe");
                    Titulo.Text = "Conecta la Askey en modo fastboot";
                    Texto.Text = "Con el sistema de fábrica se entra con los botones de volumen y encendido. En cuanto la conectes por USB, se detecta sola.";
                    Contenido.Children.Add(Chip("Esperando a la Askey…", "Acento", true));
                    if (EsperaLarga) Contenido.Children.Add(Revisar("¿No la detecta? Revisa esto:"));
                    Acciones("Ver cómo se pone en fastboot", "guia:3", "Primero tengo que abrirla y hacer el cable →", "guia:1");
                    break;
                case "usb":
                    PonerHeroe("aviso", "enchufe");
                    Titulo.Text = "El PC nota algo, pero no consigue hablar con él";
                    Texto.Text = "Windows ve un aparato USB que no responde. Suele ser un cable de solo carga, un puerto que no le va bien o el cable del Rev5 mal unido.";
                    Contenido.Children.Add(Revisar(null));
                    Acciones("Ver qué hacer", "guia:7", "Abrir el Administrador de dispositivos", "administrador");
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
                        Texto.Text = "Windows ve la Askey, pero necesita el driver de Google para hablar con ella. El instalador lo baja y se lo asigna; se hace una sola vez.";
                        Acciones("Instalar el driver", "driver", "Prefiero hacerlo a mano →", "guia:2");
                    }
                    break;
                case "edl":
                    PonerHeroe("aviso", "aviso");
                    Titulo.Text = "La Askey está en modo EDL";
                    Texto.Text = "Es el modo de emergencia del procesador, no el de instalar. Desenchúfala y entra en fastboot con los botones.";
                    Acciones("Ver cómo se pone en fastboot", "guia:3", null, null);
                    break;
                case "otro":
                    PonerHeroe("aviso", "aviso");
                    if (a.Estado == "no_valido") { Titulo.Text = "Este aparato no es un RG3205W"; Texto.Text = "Responde como «" + a.Variante + "». Este instalador solo vale para el Askey RG3205W."; }
                    else if (a.Estado == "varios") { Titulo.Text = "Hay más de un aparato"; Texto.Text = "Deja conectada solo la Askey en modo fastboot."; }
                    else { Titulo.Text = "No se puede hablar con la Askey"; Texto.Text = "fastboot ha fallado: " + a.Texto + " Desconecta y vuelve a conectar el USB."; }
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
                    Acciones(null, null, "Cancelar", "cancelar");
                    break;
                case "instalar":
                    PonerHeroe("espera", "rayo");
                    Titulo.Text = "Todo listo";
                    Texto.Text = "Se graba Home y se borran todos los datos de la Askey. Tarda unos minutos.";
                    Contenido.Children.Add(Tarjeta(Resumen(new[]
                    {
                        new[] { "Sistema", "Home " + v.Numero },
                        new[] { "Askey", a.Serie + " · desbloqueada" },
                        new[] { "Se graba", string.Join(", ", ListaParticiones(v.Carpeta)) },
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
                    Contenido.Children.Add(Enlace("Detener al terminar la imagen que se está grabando", "detener", HorizontalAlignment.Center, "0,12,0,0"));
                    Acciones(null, null, "Ver detalles", "detalles");
                    break;
                case "fin_ok":
                    PonerHeroe("hecho", "hecho");
                    Titulo.Text = "Home " + instalando.Numero + " instalado";
                    Texto.Text = "Todo grabado y comprobado. El instalador no reinicia la Askey; hazlo tú así:";
                    Contenido.Children.Add(Tarjeta(ListaPuntos(new[] { "Desconecta el USB del PC.", "Desenchufa la Askey y vuelve a enchufarla.", "El primer arranque tarda varios minutos. Después sale el asistente." }, true)));
                    Acciones("Terminar", "terminar", "Qué viene después →", "guia:6");
                    break;
                case "fin_mal":
                    bool parada = e.Motivo == "cancelado";
                    PonerHeroe(parada ? "aviso" : "fallo", parada ? "aviso" : "fallo");
                    Titulo.Text = parada ? "Instalación detenida" : "No se ha terminado";
                    Texto.Text = e.Error;
                    Contenido.Children.Add(Chip("Si la Askey sigue en fastboot, puedes volver a intentarlo", "Aviso"));
                    if (e.Motivo == "herramientas") Contenido.Children.Add(Enlace("Volver a bajar las herramientas de Google →", "herramientas", HorizontalAlignment.Center, "0,16,0,0"));
                    if (informeTexto != null) Contenido.Children.Add(Enlace("Enviar informe del fallo →", "informe", HorizontalAlignment.Center, "0,16,0,0"));
                    Acciones("Volver a intentarlo", "otra_vez", "Ver detalles", "detalles");
                    break;
            }
            Anunciar(Titulo);
            Anunciar(Texto);
            // Entrada: la pantalla llega deslizándose y aparece
            Animar(Pantalla, UIElement.OpacityProperty, 0, 1, 380);
            Animar(PantallaDesliz, TranslateTransform.XProperty, 28, 0, 420);
        }

        static IEnumerable<string> ListaParticiones(string carpeta)
        {
            try { return Imagenes.DelPaquete(carpeta).Select(p => p.Key).ToList(); }
            catch (Fallo) { return Imagenes.Particiones; }
        }

        // Lo que hay que revisar cuando el PC no ve la Askey
        Border Revisar(string titulo)
        {
            var s = new StackPanel();
            if (titulo != null) s.Children.Add(BloqueTexto(titulo, 13, "SemiBold", "Texto", "0,0,0,6"));
            s.Children.Add(ListaPuntos(new[]
            {
                "Un cable de **datos**: muchos cables solo cargan.",
                "Un puerto USB 2.0 del PC (los de detrás en un sobremesa), sin hub ni base.",
                "Conecta el USB **después** de entrar en fastboot: si no, se queda en modo carga.",
                "Rev5: revisa que **D+ y D−** no estén cruzados en el J4902."
            }, false));
            var t = Tarjeta(s);
            t.Margin = new Thickness(0, 12, 0, 0);
            return t;
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
            }
            else if (k == "driver" && e.Trabajo == "driver" && e.Texto.Length > 0) Texto.Text = e.Texto;
            var pasos = e.Pasos;
            if (panelFases != null && pasos != null)
            {
                string firma = string.Join(",", pasos.Select(x => x.Estado));
                if (firma != firmaFases) { firmaFases = firma; PintarFases(panelFases, pasos); }
            }
        }

        // ------------------------------------------------------------ hasta dónde se ha llegado (para el abandono)

        DateTime inicioSesion;
        bool algoHecho, instalacionIntentada;
        string etapa = "descargar";
        int rangoEtapa;

        void Etapa(bool listo, Aparato a)
        {
            string s = "conectar";
            int r = 1;
            if (!listo) { s = "descargar"; r = 0; }
            else switch (a.Estado)
            {
                case "ok": s = "instalar"; r = 4; break;
                case "bloqueada": s = "bloqueada"; r = 3; break;
                case "sin_driver": case "edl": case "no_valido": s = a.Estado; r = 2; break;
                case "usb_mal": s = "usb_desconocido"; r = 2; break;
            }
            if (r >= rangoEtapa) { rangoEtapa = r; etapa = s; }
            if (r >= 2) algoHecho = true;
        }

        // Al cerrar sin haber intentado instalar: hasta dónde se llegó. Solo si el servidor lo admite y tras un rato de
        // uso o algo hecho: abrir y cerrar para mirar no cuenta
        void Abandono()
        {
            if (demo != null || CasillaEstadistica.IsChecked != true || instalacionIntentada) return;
            if (!Estadistica.Acepta.Contains("abandono")) return;
            long segundos = (long)(DateTime.UtcNow - inicioSesion).TotalSeconds;
            if (!algoHecho && segundos < 120) return;
            string home = NumeroPublicado(version);
            if (home == null) return;
            Estadistica.EnviarAlCerrar(Estadistica.Mensaje(home, "abandono", etapa, segundos));
        }

        // La versión, solo si es una publicada de la lista: el servidor no acepta otras (un zip local renombrado, una de
        // prueba que ya no está)
        string NumeroPublicado(Version v)
        {
            if (v == null || !Estadistica.VersionPublicable(v.Numero)) return null;
            return e.Releases.Any(r => r.Numero == v.Numero) ? v.Numero : null;
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
            if (k == "conectar") { if (esperandoDesde == DateTime.MaxValue) esperandoDesde = DateTime.UtcNow; }
            else esperandoDesde = DateTime.MaxValue;
            string firma = k + "|" + a.Estado + "|" + a.Serie + "|" + e.GitHub + "|" + e.Herr + "|" + (k == "conectar" && EsperaLarga);
            if (firma != pantalla) { pantalla = firma; MostrarPantalla(k); }
            RefrescarPantalla(k);
            PonerBarra(k);

            var v = version;
            bool listo = v != null && v.Carpeta != null && !string.IsNullOrEmpty(e.FB);
            bool conectada = a.Estado == "ok" || a.Estado == "bloqueada";
            bool acabado = mostrarFin && finBien;
            if (demo == null) Etapa(listo, a);
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
            Registrar(bien ? "== Terminado bien." : e.Motivo == "cancelado" ? "== " + e.Error : "== Fallo: " + e.Error);
            PrepararInforme(bien);
            bool cancelado = e.Motivo == "cancelado";
            switch (trabajoVisto)
            {
                case "instalar":
                    mostrarFin = true;
                    finBien = bien;
                    break;
                case "descargar":
                    RellenarVersiones();
                    if (!bien && !cancelado) AvisoFallo(e.Error, "No se ha podido descargar");
                    break;
                case "driver":
                    if (bien) Registrar("La Askey ya funciona con el driver USB.");
                    else if (cancelado) Avisar("Driver USB", e.Error);
                    else AvisoFallo(e.Error, "El driver USB no está asignado");
                    break;
                case "desbloquear":
                    if (!bien && !cancelado) AvisoFallo(e.Error, "No se ha desbloqueado");
                    break;
            }
            ficheroRegistro = null;
            e.Fase = "visto";
        }

        // El aviso de un fallo, con lo que se puede hacer (cambiar de carpeta, volver a bajar las herramientas) o, si no,
        // la opción de enviar su informe (antes se ve qué se envía)
        void AvisoFallo(string texto, string titulo)
        {
            if (e.Motivo == "espacio") { Avisar(titulo, texto, "Cambiar carpeta", () => Opcion("cambiar")); return; }
            if (e.Motivo == "herramientas") { Avisar(titulo, texto, "Volver a bajar las herramientas", () => Accion("herramientas")); return; }
            if (informeTexto == null) { Avisar(titulo, texto); return; }
            Preguntar(titulo, texto + "\n\n¿Quieres enviar un informe de este fallo para que se pueda arreglar? Antes verás exactamente qué se envía.",
                "Ver el informe", "No, gracias", false, AbrirInforme);
        }

        // ------------------------------------------------------------ demostración

        // Modo demostración (INSTALADOR_DEMO = descargar | descargando | conectar | revisar | usb | driver | desbloquear |
        // instalar | instalando | hecho | fallo | informe | guia | opciones | sin_red | aviso | envio): solo para revisar el
        // diseño. No arranca el vigía, no toca fastboot y no envía nada
        bool demoListo;

        void Demostracion()
        {
            Ventana.Title = "Instalar Home · DEMOSTRACIÓN";
            if (demo == "sin_red")
            {
                e.GitHub = "sin_red";
                e.ErrorGitHub = "No se encuentra el servidor (fallo de DNS). Revisa la conexión; un filtro de contenidos o la red de una empresa pueden bloquearlo.";
                e.FB = "fastboot.exe"; e.Herr = "ok";
                e.Aparato = new Aparato("ninguno");
                RellenarVersiones();
                return;
            }
            e.Releases = new List<Release>
            {
                new Release
                {
                    Numero = "2.1.1", Fecha = "2026-11-15T12:00:00Z", Zip = "Home-2.1.1-RG3205W.zip", Url = "https://github.com/" + Rutas.Repo + "/releases/download/2.1.1/Home-2.1.1-RG3205W.zip", Tamano = 1373741824, Sums = "", Huella = "",
                    Notas = "### Qué trae\n\n- Compilación `user` firmada con claves propias, SELinux estricto y parche de seguridad al día.\n" +
                            "- Asistente de primer arranque: panel de Home Assistant, altavoz de casa o tablet.\n- App Actualizaciones: actualiza sin borrar datos.\n" +
                            "- Recovery TWRP con el diseño de Home.\n- Datos cifrados (FBE). Root con Magisk, apagado de serie."
                }
            };
            e.GitHub = "ok";
            e.FB = "fastboot.exe"; e.Herr = "ok";
            switch (demo)
            {
                case "descargar": case "descargando": case "conectar": case "revisar": case "guia": case "opciones": case "aviso": case "envio": e.Aparato = new Aparato("ninguno"); break;
                case "usb": e.Aparato = new Aparato("usb_mal"); break;
                case "driver": e.Aparato = new Aparato("sin_driver"); break;
                case "desbloquear": e.Aparato = new Aparato("bloqueada") { Serie = "a1b2c3d4", Variante = "Dragon eMMC" }; break;
                default: e.Aparato = new Aparato("ok") { Serie = "a1b2c3d4", Variante = "Dragon eMMC" }; break;
            }
            demoListo = demo != "descargar" && demo != "descargando";
            RellenarVersiones();
            if (demo == "revisar") esperandoDesde = DateTime.UtcNow.AddMinutes(-2);
            if (demo == "descargando")
            {
                e.NuevasFases("herramientas", "descargar", "zip", "descomprimir", "imagenes");
                e.Pasos[0].Estado = "hecho"; e.Pasos[1].Estado = "en_curso";
                e.Trabajo = "descargar"; e.Texto = "Descargando Home-2.1.1-RG3205W.zip…";
                e.Total = 1373741824; e.Bytes = 645658657; e.Progreso = 47; e.Velocidad = 9.4 * 1048576;
            }
            if (demo == "guia") AbrirGuia(1);
            if (demo == "opciones") { PintarOpciones(); Visible(CapaOpciones, true); }
            instalando = version;
            if (demo == "instalando" || demo == "hecho" || demo == "fallo")
            {
                e.NuevasFases("imagenes", "askey", "boot", "recovery", "vendor", "system", "splash", "cache", "userdata");
                string[] estados = demo == "instalando" ? new[] { "hecho", "hecho", "hecho", "hecho", "hecho", "en_curso", "espera", "espera", "espera" }
                    : demo == "hecho" ? Enumerable.Repeat("hecho", 9).ToArray()
                    : new[] { "hecho", "hecho", "hecho", "fallo", "espera", "espera", "espera", "espera", "espera" };
                for (int i = 0; i < 9; i++) e.Pasos[i].Estado = estados[i];
                e.Progreso = 47; e.Texto = "Grabando system…";
                trabajoVisto = "instalar";
                if (demo == "instalando") { e.Trabajo = "instalar"; e.Fase = "en_curso"; }
                else
                {
                    mostrarFin = true; finBien = demo == "hecho";
                    var f = Fastboot.Explicar("flash recovery", "Sending 'recovery' (28672 KB)    FAILED (Write to device failed (Unknown error))\nfastboot: error: Command failed");
                    e.Error = f.Message; e.Motivo = f.Motivo;
                }
            }
            if (demo == "fallo" || demo == "informe")
            {
                informeHome = version.Numero; informePaso = "grabar_recovery";
                informeTexto = Estadistica.Limpiar("== Home 2.1.1 · Askey a1b2c3d4 · 12-11-2026 18:40\r\nImágenes comprobadas.\r\n" +
                    "> fastboot flash boot\r\nSending 'boot' (16384 KB)    OKAY [  0.512s]\r\nWriting 'boot'    OKAY [  0.207s]\r\n" +
                    "> fastboot flash recovery\r\nSending 'recovery' (28672 KB)    FAILED (Write to device failed (Unknown error))\r\n" +
                    "fastboot: error: Command failed\r\n== Fallo: se ha cortado la comunicación por USB", "a1b2c3d4");
            }
            if (demo == "informe") AbrirInforme();
            if (demo == "envio") Ventana.Loaded += delegate { QueSeEnvia(); };
            if (demo == "aviso")
                Ventana.Loaded += delegate
                {
                    Preguntar("¿Instalar Home 2.1.1?", "Se va a instalar Home 2.1.1 en la Askey a1b2c3d4.\n\n**Se borran todos los datos del aparato.**",
                        "Instalar y borrar", "Cancelar", true, delegate { });
                };
        }
    }
}
