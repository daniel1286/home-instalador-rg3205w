// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace HomeInstalador
{
    // Lo que se le pregunta a Windows: carpetas, OneDrive, suspensión, tipo de cuenta y tamaño del texto
    static class Sistema
    {
        // ------------------------------------------------------------ carpetas

        static readonly Guid CarpetaDescargasId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr ruta);

        // La carpeta Descargas de verdad: la persona puede haberla movido a otro disco o a OneDrive
        public static string CarpetaDescargas()
        {
            IntPtr p;
            if (SHGetKnownFolderPath(CarpetaDescargasId, 0, IntPtr.Zero, out p) != 0) return null;
            try { return Marshal.PtrToStringUni(p); }
            finally { Marshal.FreeCoTaskMem(p); }
        }

        const FileAttributes AlAbrir = (FileAttributes)0x00040000, AlLeer = (FileAttributes)0x00400000;

        // Una carpeta que sincroniza OneDrive: subiría gigas de imágenes y puede bloquear los archivos mientras se usan
        public static bool EnOneDrive(string carpeta)
        {
            if (string.IsNullOrEmpty(carpeta)) return false;
            string completa;
            try { completa = Path.GetFullPath(carpeta).TrimEnd('\\') + "\\"; }
            catch (Exception) { return false; }
            foreach (string v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                string raiz = Environment.GetEnvironmentVariable(v);
                if (!string.IsNullOrEmpty(raiz) && completa.StartsWith(raiz.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            }
            // Fuera de esas rutas, lo que tenga los atributos de un archivo en la nube (también otros servicios así)
            for (string d = completa.TrimEnd('\\'); !string.IsNullOrEmpty(d); d = Path.GetDirectoryName(d))
            {
                try
                {
                    if (Directory.Exists(d) && (File.GetAttributes(d) & (AlAbrir | AlLeer)) != 0) return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return false;
        }

        // ------------------------------------------------------------ suspensión

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint estado);

        // Mientras un hilo de trabajo lo pida, el PC no se suspende (la pantalla sí se puede apagar)
        public static void SinSuspender(bool si)
        {
            const uint Continuo = 0x80000000, SinDormir = 0x00000001;
            SetThreadExecutionState(si ? Continuo | SinDormir : Continuo);
        }

        // ------------------------------------------------------------ cuenta

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr proceso, uint acceso, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool GetTokenInformation(IntPtr token, int clase, out int valor, int tam, out int devuelto);

        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);

        // Una cuenta estándar: Windows le pide la contraseña de otra persona para instalar drivers
        public static bool CuentaEstandar()
        {
            IntPtr token;
            if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0008, out token)) return false;
            try
            {
                int tipo, n;
                // TokenElevationType: 1 = sin división (cuenta estándar, o UAC apagado), 2 = elevada, 3 = administrador limitado
                if (!GetTokenInformation(token, 18, out tipo, 4, out n)) return false;
                return tipo == 1 && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            finally { CloseHandle(token); }
        }

        // ------------------------------------------------------------ texto

        // «Tamaño del texto» de Accesibilidad (100 a 225 %). WPF con .NET Framework no lo aplica solo
        public static double EscalaTexto()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility"))
                {
                    object v = k != null ? k.GetValue("TextScaleFactor") : null;
                    if (v is int) return Math.Max(1.0, Math.Min(2.25, (int)v / 100.0));
                }
            }
            catch (System.Security.SecurityException) { }
            return 1.0;
        }

        // ------------------------------------------------------------ ventana

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr h, int como);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr h);

        // Si el instalador ya está abierto, se trae al frente en vez de abrir otro
        public static bool TraerAlFrente()
        {
            var yo = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(yo.ProcessName))
                using (p)
                {
                    if (p.Id == yo.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                    if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9);
                    return SetForegroundWindow(p.MainWindowHandle);
                }
            return false;
        }
    }
}
