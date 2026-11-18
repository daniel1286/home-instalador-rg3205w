// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

[assembly: System.Reflection.AssemblyTitle("Instalar Home")]
[assembly: System.Reflection.AssemblyDescription("Instalador de Home para el Askey RG3205W")]
[assembly: System.Reflection.AssemblyProduct("Home")]
[assembly: System.Reflection.AssemblyCopyright("2026 The Home Project · Apache-2.0")]
[assembly: System.Reflection.AssemblyVersion("2.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.2.0.0")]
// Sin esto, WPF trata el .exe como uno antiguo y no reescala la ventana al pasarla a una pantalla con otra escala
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace HomeInstalador
{
    static class Programa
    {
        static Instalador instalador;

        [STAThread]
        static int Main(string[] args)
        {
            // El mismo .exe, con permiso de administrador, asigna el driver USB (DriverUsb)
            if (args.Length == 3 && args[0] == DriverUsb.Argumento) return DriverUsb.Elevado(args[1], args[2]);

            AppDomain.CurrentDomain.UnhandledException += (s, a) => Apuntar(a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { Apuntar(a.Exception); a.SetObserved(); };

            // Dos instaladores a la vez se pisarían con fastboot: si ya hay uno, se trae al frente
            bool primero;
            using (new Mutex(true, "Local\\InstalarHome", out primero))
            {
                if (!primero)
                {
                    if (!Sistema.TraerAlFrente()) Instalador.Suelto("Instalar Home", "El instalador de Home ya está abierto.");
                    return 1;
                }
                Red.Preparar();
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, a) =>
                {
                    Fallo(a.Exception);
                    a.Handled = true;
                };
                try { instalador = new Instalador(Environment.GetEnvironmentVariable("INSTALADOR_DEMO")); }
                catch (Exception ex) { Fallo(ex); return 1; }
                return app.Run(instalador.Ventana);
            }
        }

        static void Apuntar(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Rutas.Registros);
                File.AppendAllText(Path.Combine(Rutas.Registros, "errores.txt"), DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss") + "\r\n" + Rutas.SinUsuario(ex.ToString()) + "\r\n\r\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Un fallo sin controlar: se guarda en registros y se enseña, sin cerrar la ventana
        public static void Fallo(Exception ex)
        {
            Apuntar(ex);
            string texto = "Algo ha fallado en el instalador:\n\n" + ex.Message + "\n\nEl detalle está en la carpeta de registros («Opciones» → «Registros»).";
            var i = instalador;
            if (i != null && i.Ventana != null && Application.Current != null)
                i.Ventana.Dispatcher.BeginInvoke(new Action(() => i.Avisar("Algo ha fallado", texto)));
            else Instalador.Suelto("Instalar Home", texto);
        }
    }
}
