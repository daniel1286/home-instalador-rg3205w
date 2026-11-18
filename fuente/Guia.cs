// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace HomeInstalador
{
    // Guía desde el sistema de fábrica: lo mismo que guias/INSTALAR.md, pero contado para este instalador. Todas las
    // imágenes son dibujos propios (recursos/guia/fuentes) y van dentro del .exe
    partial class Instalador
    {
        class Capitulo
        {
            public string Titulo;
            public Func<UIElement[]> Bloques;
            public Capitulo(string titulo, Func<UIElement[]> bloques) { Titulo = titulo; Bloques = bloques; }
        }

        List<Capitulo> guia;

        const string Herramientas = @"%LOCALAPPDATA%\Home\Instalador\herramientas";

        // Lo que pesa Home según la lista de versiones
        string PesoHome()
        {
            var v = version;
            return v != null && v.Remoto != null && v.Remoto.Tamano > 0 ? Imagenes.TamanoTexto(v.Remoto.Tamano) : "algo más de 1 GB";
        }

        List<Capitulo> Capitulos()
        {
            return new List<Capitulo>
            {
                new Capitulo("1. Antes de empezar", () => new UIElement[]
                {
                    BloqueTexto("Esta guía lleva un Askey RG3205W con su sistema de fábrica hasta Home. El instalador descarga Home, comprueba cada archivo y lo graba; tú solo tienes que abrir el aparato, conectarlo al PC y ponerlo en modo fastboot."),
                    BloqueImagen("proceso.png", null, "Siete pasos: descargar, conectar, drivers, fastboot, desbloquear, instalar y asistente."),
                    BloqueAviso(true, "La instalación **borra todos los datos** del aparato. Abrirlo y cambiarle el software puede anular la garantía. Sigue la guía bajo tu responsabilidad."),
                    BloqueTexto("Qué necesitas", 15, "Bold"),
                    BloqueTabla(
                        new[] { "", "Detalle" },
                        new[] { "Askey RG3205W", "Rev4 o Rev5 (en el capítulo 2 se ve cuál es)" },
                        new[] { "PC", "Windows 10 u 11 de 64 bits, con unos 4 GB libres fuera de OneDrive" },
                        new[] { "Cable", "Un cable USB de datos. Rev4: USB-C normal. Rev5: un cable USB unido al conector interno J4902" },
                        new[] { "Internet", "Para bajar Home (" + PesoHome() + ") y las herramientas de Google" },
                        new[] { "Tiempo", "Entre 30 y 45 minutos la primera vez" }),
                    BloqueAviso(false, "Empieza la descarga en el instalador y, mientras baja, ve haciendo los capítulos 2 a 4.")
                }),
                new Capitulo("2. Abrir y conectar", () => new UIElement[]
                {
                    BloqueTexto("Hay dos revisiones de la placa y cada una se conecta al PC de una forma. La etiqueta no sirve para saber cuál tienes: hay que quitar la tapa trasera."),
                    BloquePasos(1,
                        "Suelta las 10 pestañas de plástico de los bordes de la tapa trasera.",
                        "Quita los 8 tornillos que hay debajo de la tapa.",
                        "Quita los 4 tornillos escondidos bajo la tira de goma negra de la parte de abajo."),
                    BloqueTexto("Mira la serigrafía de abajo a la derecha de la placa, donde pone **Rev4** o **Rev5**, y la esquina de arriba a la derecha:"),
                    BloqueImagen("placa.png", null, "Rev4: con un conector USB-C soldado arriba a la derecha. Rev5: ese hueco está vacío y al lado está el conector J4902."),
                    BloqueTabla(
                        new[] { "Revisión", "Conexión al PC" },
                        new[] { "Rev4", "Cable USB-C normal, sin más" },
                        new[] { "Rev5", "Conector interno J4902: hace falta el cable de abajo" }),
                    BloqueTexto("El cable para el Rev5", 15, "Bold"),
                    BloqueTexto("El **J4902** es el conector blanco de 4 pines (JST-PH 2.0) que hay cerca del hueco del USB-C. Lleva las cuatro líneas del USB, así que basta con unirlo a un cable USB normal."),
                    BloqueImagen("j4902.png", null, "Cable USB unido al J4902: de izquierda a derecha, D− blanco, D+ verde, GND negro y +5V rojo."),
                    BloqueImagen("j4902_pines.png", null, "El J4902 visto desde arriba, junto al hueco del USB-C, con el orden de sus pines."),
                    BloquePasos(1,
                        "Corta un cable USB que no uses y quédate con el lado de la clavija USB-A (la que va al PC).",
                        "Pela los cuatro hilos de dentro: lo normal es rojo, blanco, verde y negro.",
                        "Une cada hilo a su pin con un conector **JST-PH 2.0 macho de 4 pines** (o cuatro terminales hembra de paso 2,0 mm): blanco a D−, verde a D+, negro a GND y rojo a +5V.",
                        "Comprueba con un polímetro que cada pin llega al contacto correcto de la clavija USB.",
                        "Enchúfalo en el J4902 con los pines en el orden del dibujo."),
                    BloqueAviso(true, "No todos los cables respetan los colores. Compruébalo siempre con el polímetro: si cruzas +5V y GND puedes estropear la placa."),
                    BloqueTexto("Las revisiones de la placa y el J4902 los documentó zry98 en GitHub.", 12, "Normal", "Texto2"),
                    BloqueBoton("Abrir su perfil en GitHub", "web:" + Rutas.Hardware)
                }),
                new Capitulo("3. Driver USB", () => new UIElement[]
                {
                    BloqueTexto("Windows necesita el **driver USB de Google** para hablar con la Askey en modo fastboot. Se hace una sola vez, con la Askey ya en fastboot (capítulo 4) y conectada al PC."),
                    BloqueTexto("Desde el instalador", 15, "Bold"),
                    BloquePasos(1,
                        "Si el instalador dice «Falta el driver USB», pulsa **Instalar el driver**.",
                        "Windows pide permiso de administrador: pulsa **Sí**. En una cuenta estándar, quien administre el PC escribe su contraseña.",
                        "El instalador baja el driver de Google, se lo asigna a la Askey y comprueba que Windows ya la ve bien. Pasa solo al paso siguiente."),
                    BloqueAviso(false, "Si el instalador dice que no ha podido asignarlo, hazlo a mano con los cuatro pasos de abajo. Solo hay que hacerlo una vez."),
                    BloqueTexto("Si hace falta: asignarlo a mano", 15, "Bold"),
                    BloqueTexto("Pulsa antes **Instalar el driver** aunque no baste: es lo que deja el driver en la carpeta del paso 3.", 12, "Normal", "Texto2"),
                    BloqueBoton("Abrir el Administrador de dispositivos", "administrador"),
                    BloquePasos(1, "En **Otros dispositivos** está «Android», con un triángulo amarillo: clic derecho → **Actualizar controlador**."),
                    BloqueImagen("driver_1.png", null, "Administrador de dispositivos: Otros dispositivos, Android, Actualizar controlador."),
                    BloquePasos(2, "**Buscar controladores en mi equipo** → **Elegir en una lista de controladores disponibles en el equipo**. Si pregunta el tipo de dispositivo: **Mostrar todos los dispositivos** → **Siguiente**."),
                    BloqueImagen("driver_2.png", null, "Buscar controladores en mi equipo y elegir en una lista."),
                    BloquePasos(3, "**Usar disco…** → **Examinar…**. En «Nombre» pega esta ruta y pulsa Intro; elige **android_winusb.inf** → **Abrir** → **Aceptar**:"),
                    BloqueCodigo(Herramientas + @"\usb_driver"),
                    BloqueImagen("driver_3.png", null, "Usar disco, Examinar y elegir android_winusb.inf."),
                    BloquePasos(4, "Elige **Android Bootloader Interface** → **Siguiente**. Si Windows avisa de que puede no ser compatible, pulsa **Sí**. Al terminar, el instalador ya ve la Askey."),
                    BloqueImagen("driver_4.png", null, "Elegir Android Bootloader Interface y Siguiente.")
                }),
                new Capitulo("4. Modo fastboot", () => new UIElement[]
                {
                    BloqueTexto("Con el sistema de fábrica, al modo fastboot se llega desde su menú de recuperación (recovery). Para abrirlo hay que hacer **dos combinaciones de botones seguidas**, y lo importante es soltar en el momento justo."),
                    BloqueAviso(true, "Conecta el USB al PC **después** de entrar en fastboot. Si el aparato arranca con el USB del PC conectado, se queda en modo carga."),
                    BloqueTexto("Los botones", 15, "Bold"),
                    BloqueTabla(
                        new[] { "Botón", "Dónde está" },
                        new[] { "Vol + y Vol −", "Arriba, en el canto superior" },
                        new[] { "Encendido", "Detrás: el botón blanco, al lado de la toma de corriente" }),
                    BloqueTexto("Primera combinación: hasta que la pantalla se quede en negro", 15, "Bold"),
                    BloqueImagen("recovery_1.png", null, "Apagado; mantener los tres botones; pantalla azul sin soltar; pantalla negra y soltar."),
                    BloquePasos(1,
                        "**Apaga el aparato.** Si no puedes apagarlo desde la pantalla, desenchúfalo de la corriente.",
                        "**Mantén pulsados a la vez Vol +, Vol − y Encendido.** Si lo has desenchufado, vuelve a enchufarlo ahora, sin soltar los botones.",
                        "Sale la **pantalla azul con el logo blanco**. **No sueltes todavía.**",
                        "El logo desaparece y la pantalla se queda **en negro, con un poco de luz**. **Suelta los tres botones enseguida.**"),
                    BloqueTexto("Segunda combinación: abrir el menú", 15, "Bold"),
                    BloqueImagen("recovery_2.png", null, "Mantener Encendido y pulsar Vol + una vez; sale el menú."),
                    BloquePasos(5,
                        "Vuelve a **mantener pulsado Encendido** y, sin soltarlo, **pulsa Vol + una vez**.",
                        "Sale el **menú de recovery**. Ya puedes soltar Encendido."),
                    BloqueAviso(false, "Si no sale el menú, vuelve al paso 1. Lo normal es soltar demasiado pronto o demasiado tarde: suelta justo cuando la pantalla pase **de azul a negro**. Con la tapa trasera quitada, los botones se pulsan mejor."),
                    BloqueTexto("Elegir el modo fastboot", 15, "Bold"),
                    BloqueImagen("recovery_menu.png", null, "Menú de recovery con Reboot to bootloader marcado."),
                    BloquePasos(7,
                        "Con **Vol +** y **Vol −** baja hasta **Reboot to bootloader**. En este menú la pantalla táctil no funciona.",
                        "Pulsa **Encendido** para elegirla.",
                        "Cuando salga el modo fastboot, **conecta el USB al PC**. El instalador detecta la Askey y pasa al siguiente paso."),
                    BloqueTexto("Si ya tienes Home instalado es más fácil: en su recovery (TWRP), **Reiniciar → Bootloader**.", 12, "Normal", "Texto2")
                }),
                new Capitulo("5. Desbloquear", () => new UIElement[]
                {
                    BloqueTexto("Para grabar Home, el bootloader tiene que estar desbloqueado. El instalador lo mira al conectar la Askey y, si hace falta, te enseña el botón **Desbloquear**."),
                    BloqueTexto("Si en la pantalla de la Askey aparece una pregunta, acéptala con los botones de volumen y encendido. Tienes dos minutos; si no, el instalador lo da por cancelado y puedes volver a pulsar Desbloquear."),
                    BloqueAviso(true, "No vuelvas a bloquear el bootloader (fastboot flashing lock u oem lock) con Home instalado: el aparato dejaría de arrancar."),
                    BloqueTexto("Lo que hace el instalador, por si prefieres hacerlo a mano desde " + Herramientas + @"\platform-tools:", 12, "Normal", "Texto2"),
                    BloqueCodigo(".\\fastboot.exe getvar unlocked\r\n.\\fastboot.exe flashing unlock\r\n.\\fastboot.exe oem unlock")
                }),
                new Capitulo("6. Instalar", () => new UIElement[]
                {
                    BloqueTexto("Con la Askey conectada y desbloqueada, pulsa **Instalar Home**. El instalador:"),
                    BloquePasos(1,
                        "Comprueba la huella SHA256 de cada imagen y que cabe en su partición.",
                        "Comprueba que es un RG3205W desbloqueado y que sigue conectado.",
                        "Graba boot, recovery, vendor, system y splash.",
                        "Formatea cache y userdata (aquí se borran los datos)."),
                    BloqueTexto("No toca las particiones con datos propios de tu aparato (persist, modem, calibraciones). El recovery de Home se instala siempre porque es el que aplica las actualizaciones. Tarda unos minutos y, mientras tanto, el PC no se suspende."),
                    BloqueAviso(true, "Mientras graba, no desconectes el USB ni cierres la ventana. Si tienes que parar, pulsa **Detener**: se termina la imagen que se está grabando y no se sigue."),
                    BloqueTexto("El mensaje «Invalid sparse file format at header magic» de los detalles es normal: fastboot parte system.img en trozos para enviarla.", 12, "Normal", "Texto2")
                }),
                new Capitulo("7. Primer arranque", () => new UIElement[]
                {
                    BloqueTexto("El instalador no reinicia la Askey. Al terminar:"),
                    BloquePasos(1, "Desconecta el USB del PC.", "Desenchufa el aparato y vuelve a enchufarlo."),
                    BloqueTexto("Sale el splash de Home y después la animación de arranque. La primera vez tarda varios minutos, porque Android prepara las aplicaciones y cifra los datos. Es normal."),
                    BloqueImagen("primer_arranque.png", null, "Primer arranque: splash de Home, animación de varios minutos y asistente."),
                    BloqueTexto("Al terminar se abre el asistente: idioma, región y hora, Wi-Fi, nombre, qué instalar (panel de Home Assistant, altavoz de casa, VNC, F-Droid, Aurora Store), pantalla, privacidad, opciones avanzadas (ADB y root) y resumen."),
                    BloqueBoton("Probar el asistente en el navegador", "web:" + Rutas.Demo),
                    BloqueTexto("Las versiones nuevas las instala la app **Actualizaciones** sin borrar datos, así que este instalador solo hace falta una vez.", 12, "Normal", "Texto2")
                }),
                new Capitulo("8. Si algo va mal", () => new UIElement[]
                {
                    BloqueTabla(
                        new[] { "Qué pasa", "Qué hacer" },
                        new[] { "No detecta la Askey", "Cable de datos (no de solo carga), otro puerto USB 2.0 sin hub, y conectar después de entrar en fastboot" },
                        new[] { "«El PC nota algo, pero no consigue hablar con él»", "Cambia el cable o el puerto. Rev5: revisa que D+ y D− no estén cruzados" },
                        new[] { "«Falta el driver USB»", "Pulsa Instalar el driver o hazlo a mano (capítulo 3)" },
                        new[] { "«Está en modo EDL»", "Has entrado en el modo de emergencia: desenchufa y repite el capítulo 4" },
                        new[] { "«No es un RG3205W»", "Este instalador solo vale para el Askey RG3205W" },
                        new[] { "«Otros programas usando fastboot o adb»", "Ciérralos o deja que el instalador los cierre" },
                        new[] { "«Se ha cortado la comunicación por USB»", "Otro puerto (mejor USB 2.0) u otro cable; vuelve a poner la Askey en fastboot" },
                        new[] { "«El antivirus ha quitado fastboot»", "Vuelve a bajar las herramientas en «Opciones» y añade su carpeta a las exclusiones del antivirus" },
                        new[] { "«Falta espacio»", "Libera espacio o cambia la carpeta de descargas en «Opciones»" },
                        new[] { "El archivo llega dañado", "El instalador lo descarga otra vez solo; si vuelve a pasar, revisa la conexión o prueba otra red" },
                        new[] { "Los botones no sacan el menú", "Repite el capítulo 4 con calma; con la tapa quitada se pulsan mejor" },
                        new[] { "Se queda en modo carga", "Desconecta el USB del PC y vuelve a enchufar la corriente" },
                        new[] { "La animación dura más de 15 min", "Vuelve a fastboot e instala otra vez" },
                        new[] { "No arranca ni entra en fastboot", "Rescate por EDL: capítulo 9" }),
                    BloqueTexto("Si no lo consigues, abre una incidencia en GitHub con tu revisión (Rev4 o Rev5), el paso en el que estabas y el código de **Enviar informe del fallo** (o, si prefieres no enviarlo, el registro de «Opciones» → «Registros»)."),
                    BloqueBoton("Abrir una incidencia", "web:" + Rutas.Incidencias)
                }),
                new Capitulo("9. Copia y rescate (EDL)", () => new UIElement[]
                {
                    BloqueTexto("EDL es el modo de emergencia del procesador: funciona aunque el sistema o el bootloader estén dañados y permite leer y escribir cualquier partición. Es para usuarios con algo de experiencia, pero vale la pena hacer una copia completa antes de instalar."),
                    BloqueTexto("Con ella guardas persist y otras particiones con datos únicos de tu aparato (direcciones MAC y calibraciones) que no se pueden sacar de otra unidad."),
                    BloqueAviso(true, "No escribas particiones por EDL sin una copia completa de tu propio aparato."),
                    BloqueBoton("Ver cómo hacerlo en la guía completa", "web:" + Rutas.GuiaWeb + "#11-copia-de-seguridad-y-rescate-por-edl")
                })
            };
        }

        void PintarCapitulo(int i)
        {
            ContenidoGuia.Children.Clear();
            ContenidoGuia.Children.Add(BloqueTexto(guia[i].Titulo, 22, "Bold", "Texto", "0,0,0,12"));
            foreach (var b in guia[i].Bloques()) if (b != null) ContenidoGuia.Children.Add(b);
            if (i < guia.Count - 1)
            {
                int sig = i + 1;
                string nombre = guia[sig].Titulo.Substring(guia[sig].Titulo.IndexOf(' ') + 1);
                var boton = new Button
                {
                    Style = (Style)Ventana.FindResource("Enlace"), Content = "Siguiente: " + nombre + "  →",
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0)
                };
                boton.Click += delegate { ((RadioButton)IndiceGuia.Children[sig]).IsChecked = true; };
                ContenidoGuia.Children.Add(boton);
            }
            ScrollGuia.ScrollToTop();
            Animar(ContenidoGuia, UIElement.OpacityProperty, 0, 1, 300);
        }

        void CrearGuia()
        {
            guia = Capitulos();
            for (int i = 0; i < guia.Count; i++)
            {
                int n = i;
                var r = new RadioButton { Style = (Style)Ventana.FindResource("Indice"), GroupName = "guia", Content = guia[i].Titulo };
                r.Checked += delegate { PintarCapitulo(n); };
                IndiceGuia.Children.Add(r);
            }
            ((RadioButton)IndiceGuia.Children[0]).IsChecked = true;
        }
    }
}
