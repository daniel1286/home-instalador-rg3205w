// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows;

[assembly: System.Reflection.AssemblyTitle("Instalar Home")]
[assembly: System.Reflection.AssemblyDescription("Instalador de Home para el Askey RG3205W")]
[assembly: System.Reflection.AssemblyProduct("Home")]
[assembly: System.Reflection.AssemblyCopyright("2026 The Home Project · Apache-2.0")]

namespace HomeInstalador
{
    static class Programa
    {
        [STAThread]
        static int Main()
        {
            // Dos instaladores a la vez se pisarían con fastboot
            bool primero;
            using (new Mutex(true, "Local\\InstalarHome", out primero))
            {
                if (!primero)
                {
                    MessageBox.Show("El instalador de Home ya está abierto.", "Instalar Home", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 1;
                }
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += (s, a) =>
                {
                    Fallo(a.Exception);
                    a.Handled = true;
                };
                Instalador instalador;
                try { instalador = new Instalador(Environment.GetEnvironmentVariable("INSTALADOR_DEMO")); }
                catch (Exception ex) { Fallo(ex); return 1; }
                return app.Run(instalador.Ventana);
            }
        }

        // Un fallo sin controlar: se guarda en registros y se enseña, sin cerrar la ventana
        static void Fallo(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Rutas.Registros);
                File.AppendAllText(Path.Combine(Rutas.Registros, "errores.txt"), DateTime.Now.ToString("dd-MM-yyyy HH:mm:ss") + "\r\n" + Rutas.SinUsuario(ex.ToString()) + "\r\n\r\n");
            }
            catch (Exception) { }
            MessageBox.Show("Algo ha fallado en el instalador:\n\n" + ex.Message + "\n\nEl detalle está en la carpeta de registros.", "Instalar Home", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
