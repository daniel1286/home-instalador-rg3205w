// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace HomeInstalador
{
    // El driver USB de Google para la Askey en fastboot. Su android_winusb.inf no nombra el VID/PID de la Askey
    // (18D1:D00D), así que instalarlo con pnputil lo deja en Windows sin dárselo a nadie. Aquí se hace lo mismo que
    // «Usar disco…» en el Administrador de dispositivos: se elige «Android Bootloader Interface» de ese .inf para la
    // Askey. Lo hace el propio .exe con un argumento, en un proceso aparte con permiso de administrador
    static class DriverUsb
    {
        public const string Argumento = "--asignar-driver";
        const string IdAskey = @"USB\VID_18D1&PID_D00D";
        const string Interfaz = "Android Bootloader Interface";

        // Respuestas del proceso de administrador (su código de salida)
        public const int Ok = 0, OkReiniciar = 1, SinAparato = 2, SinDriver = 3, Error = 10;

        // ------------------------------------------------------------ en el proceso del instalador

        // Lanza el proceso de administrador y espera su respuesta. Win32Exception 1223 si la persona dice que no
        public static int Asignar(string inf, out string detalle)
        {
            string salida = Path.Combine(Rutas.Datos, "driver_resultado.txt");
            if (File.Exists(salida)) File.Delete(salida);
            string exe = Assembly.GetExecutingAssembly().Location;
            var psi = new ProcessStartInfo(exe, Argumento + " " + Fastboot.Argumento(inf) + " " + Fastboot.Argumento(salida))
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            int codigo;
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(300000))
                {
                    detalle = "El proceso de administrador no ha terminado en 5 minutos.";
                    return Error;
                }
                codigo = p.ExitCode;
            }
            detalle = "";
            try { if (File.Exists(salida)) { detalle = File.ReadAllText(salida).Trim(); File.Delete(salida); } }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return codigo;
        }

        // ------------------------------------------------------------ en el proceso de administrador

        public static int Elevado(string inf, string salida)
        {
            int codigo;
            string detalle;
            try { codigo = AsignarAqui(inf, out detalle); }
            catch (Exception ex) { codigo = Error; detalle = ex.GetType().Name + ": " + ex.Message; }
            try { File.WriteAllText(salida, detalle, Encoding.UTF8); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return codigo;
        }

        static int AsignarAqui(string inf, out string detalle)
        {
            detalle = "";
            if (!File.Exists(inf)) { detalle = "No está " + inf; return Error; }
            Guid clase;
            var nombreClase = new StringBuilder(64);
            int necesario;
            if (!SetupDiGetINFClass(inf, out clase, nombreClase, nombreClase.Capacity, out necesario))
                return Fallo("leer la clase del .inf", out detalle);

            // Dos vueltas: si la Askey no está en la clase del driver («Otros dispositivos»), se pone en ella y se vuelve
            // a leer, como hace el asistente de Windows al elegir «Mostrar todos los dispositivos»
            for (int vuelta = 0; vuelta < 2; vuelta++)
            {
                IntPtr lista = SetupDiGetClassDevs(IntPtr.Zero, "USB", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
                if (lista == new IntPtr(-1)) return Fallo("leer los aparatos USB", out detalle);
                try
                {
                    var d = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                    bool encontrada = false;
                    for (int i = 0; SetupDiEnumDeviceInfo(lista, i, ref d); i++)
                    {
                        if (!EsAskey(lista, ref d)) continue;
                        encontrada = true;
                        if (d.ClassGuid == clase) return Instalar(lista, ref d, inf, out detalle);
                        if (vuelta == 1) { detalle = "La Askey no ha cambiado de clase."; return Error; }
                        byte[] g = Encoding.Unicode.GetBytes(clase.ToString("B") + "\0");
                        if (!SetupDiSetDeviceRegistryProperty(lista, ref d, SPDRP_CLASSGUID, g, g.Length))
                            return Fallo("poner la Askey en la clase del driver", out detalle);
                        break;
                    }
                    if (!encontrada) return SinAparato;
                }
                finally { SetupDiDestroyDeviceInfoList(lista); }
            }
            return Error;
        }

        static bool EsAskey(IntPtr lista, ref SP_DEVINFO_DATA d)
        {
            var buf = new byte[4096];
            int tipo, necesario;
            if (!SetupDiGetDeviceRegistryProperty(lista, ref d, SPDRP_HARDWAREID, out tipo, buf, buf.Length, out necesario)) return false;
            foreach (string id in Encoding.Unicode.GetString(buf, 0, Math.Min(necesario, buf.Length)).Split('\0'))
                if (id.StartsWith(IdAskey, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static int Instalar(IntPtr lista, ref SP_DEVINFO_DATA d, string inf, out string detalle)
        {
            detalle = "";
            var p = new SP_DEVINSTALL_PARAMS { cbSize = Marshal.SizeOf(typeof(SP_DEVINSTALL_PARAMS)) };
            if (!SetupDiGetDeviceInstallParams(lista, ref d, ref p)) return Fallo("leer los parámetros", out detalle);
            p.Flags |= DI_ENUMSINGLEINF;
            p.FlagsEx |= DI_FLAGSEX_ALLOWEXCLUDEDDRVS;
            p.DriverPath = inf;
            if (!SetupDiSetDeviceInstallParams(lista, ref d, ref p)) return Fallo("elegir el .inf", out detalle);
            if (!SetupDiBuildDriverInfoList(lista, ref d, SPDIT_CLASSDRIVER)) return Fallo("leer los drivers del .inf", out detalle);

            var drv = new SP_DRVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DRVINFO_DATA)) };
            bool elegido = false;
            for (int i = 0; SetupDiEnumDriverInfo(lista, ref d, SPDIT_CLASSDRIVER, i, ref drv); i++)
            {
                if (!string.Equals(drv.Description, Interfaz, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SetupDiSetSelectedDriver(lista, ref d, ref drv)) return Fallo("elegir " + Interfaz, out detalle);
                elegido = true;
                break;
            }
            if (!elegido) { detalle = "El .inf no trae «" + Interfaz + "»."; return SinDriver; }

            bool reiniciar;
            if (!DiInstallDevice(IntPtr.Zero, lista, ref d, IntPtr.Zero, 0, out reiniciar)) return Fallo("instalar el driver", out detalle);
            return reiniciar ? OkReiniciar : Ok;
        }

        static int Fallo(string que, out string detalle)
        {
            int w = Marshal.GetLastWin32Error();
            detalle = "Windows no ha podido " + que + ": " + new Win32Exception(w).Message + " (" + w + ").";
            return Error;
        }

        // ------------------------------------------------------------ SetupAPI

        const int DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
        const int SPDRP_HARDWAREID = 0x1, SPDRP_CLASSGUID = 0x8;
        const int SPDIT_CLASSDRIVER = 0x1;
        const int DI_ENUMSINGLEINF = 0x00010000, DI_FLAGSEX_ALLOWEXCLUDEDDRVS = 0x00000800;

        // Con la alineación por defecto miden lo mismo que en setupapi.h en 32 y en 64 bits
        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SP_DEVINSTALL_PARAMS
        {
            public int cbSize, Flags, FlagsEx;
            public IntPtr hwndParent, InstallMsgHandler, InstallMsgHandlerContext, FileQueue;
            public UIntPtr ClassInstallReserved;
            public int Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath;
        }

        // SP_DRVINFO_DATA_V1: sin fecha ni versión, que en 32 bits cambiarían la alineación
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SP_DRVINFO_DATA
        {
            public int cbSize, DriverType;
            public UIntPtr Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string MfgName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProviderName;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetINFClassW")]
        static extern bool SetupDiGetINFClass(string inf, out Guid clase, StringBuilder nombre, int tam, out int necesario);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
        static extern IntPtr SetupDiGetClassDevs(IntPtr clase, string enumerador, IntPtr ventana, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr lista, int indice, ref SP_DEVINFO_DATA d);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr lista);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
        static extern bool SetupDiGetDeviceRegistryProperty(IntPtr lista, ref SP_DEVINFO_DATA d, int propiedad, out int tipo, byte[] buf, int tam, out int necesario);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiSetDeviceRegistryPropertyW")]
        static extern bool SetupDiSetDeviceRegistryProperty(IntPtr lista, ref SP_DEVINFO_DATA d, int propiedad, byte[] buf, int tam);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInstallParamsW")]
        static extern bool SetupDiGetDeviceInstallParams(IntPtr lista, ref SP_DEVINFO_DATA d, ref SP_DEVINSTALL_PARAMS p);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiSetDeviceInstallParamsW")]
        static extern bool SetupDiSetDeviceInstallParams(IntPtr lista, ref SP_DEVINFO_DATA d, ref SP_DEVINSTALL_PARAMS p);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiBuildDriverInfoList(IntPtr lista, ref SP_DEVINFO_DATA d, int tipo);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiEnumDriverInfoW")]
        static extern bool SetupDiEnumDriverInfo(IntPtr lista, ref SP_DEVINFO_DATA d, int tipo, int indice, ref SP_DRVINFO_DATA drv);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiSetSelectedDriverW")]
        static extern bool SetupDiSetSelectedDriver(IntPtr lista, ref SP_DEVINFO_DATA d, ref SP_DRVINFO_DATA drv);

        [DllImport("newdev.dll", SetLastError = true)]
        static extern bool DiInstallDevice(IntPtr ventana, IntPtr lista, ref SP_DEVINFO_DATA d, IntPtr drv, int flags, out bool reiniciar);
    }
}
