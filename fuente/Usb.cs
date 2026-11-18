// SPDX-FileCopyrightText: 2026 The Home Project
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Management;

namespace HomeInstalador
{
    // Lo que ve Windows por USB, sin abrir nada: sirve para saber qué pasa cuando fastboot no ve la Askey
    static class Usb
    {
        // La Askey en modo fastboot (VID 18D1, PID D00D) y en modo de emergencia (EDL)
        const string Fastboot = "VID[_]18D1&PID[_]D00D", Edl = "VID[_]05C6&PID[_]9008";

        static List<ManagementObject> Buscar(string donde, string campos = "DeviceID, ConfigManagerErrorCode")
        {
            var l = new List<ManagementObject>();
            using (var s = new ManagementObjectSearcher("SELECT " + campos + " FROM Win32_PnPEntity WHERE " + donde))
            using (var r = s.Get())
                foreach (ManagementObject o in r) l.Add(o);
            return l;
        }

        static uint Codigo(ManagementObject o)
        {
            object c = o["ConfigManagerErrorCode"];
            return c == null ? 0u : Convert.ToUInt32(c);
        }

        // null si la Askey no está conectada en fastboot; si está, su código del Administrador de dispositivos (0 = va bien)
        public static uint? CodigoAskey()
        {
            try
            {
                uint? peor = null;
                foreach (var o in Buscar("DeviceID LIKE '%" + Fastboot + "%'"))
                    using (o)
                    {
                        uint c = Codigo(o);
                        if (peor == null || c != 0) peor = c;
                    }
                return peor;
            }
            catch (ManagementException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }
        }

        public static bool AskeyPresente() { return CodigoAskey() != null; }

        // Aparatos USB que Windows no consigue leer (código 43, o el «desconocido» VID_0000) que ya estaban al abrir el
        // instalador: no son la Askey y no deben confundir
        static HashSet<string> previos;

        static HashSet<string> Desconocidos()
        {
            var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in Buscar("DeviceID LIKE '%VID[_]0000%' OR (DeviceID LIKE 'USB%' AND ConfigManagerErrorCode = 43)", "DeviceID"))
                using (o) h.Add(Convert.ToString(o["DeviceID"]));
            return h;
        }

        public static void Empezar()
        {
            try { previos = Desconocidos(); }
            catch (ManagementException) { previos = new HashSet<string>(); }
            catch (System.Runtime.InteropServices.COMException) { previos = new HashSet<string>(); }
        }

        // Sin nada en fastboot: sin_driver | edl | usb_mal (algo enchufado que Windows no puede leer) | ninguno
        public static string Estado()
        {
            try
            {
                bool sinDriver = false, edl = false;
                foreach (var o in Buscar("DeviceID LIKE '%" + Fastboot + "%' OR DeviceID LIKE '%" + Edl + "%'"))
                    using (o)
                    {
                        string id = Convert.ToString(o["DeviceID"]).ToUpperInvariant();
                        if (id.Contains("PID_9008")) edl = true;
                        else if (Codigo(o) != 0) sinDriver = true;
                    }
                if (edl) return "edl";
                if (sinDriver) return "sin_driver";
                if (previos != null)
                    foreach (string id in Desconocidos())
                        if (!previos.Contains(id)) return "usb_mal";
                return "ninguno";
            }
            catch (ManagementException) { return "ninguno"; }
            catch (System.Runtime.InteropServices.COMException) { return "ninguno"; }
        }

        // Otros fastboot o adb en marcha que no son del instalador: se pueden quedar con la Askey a mitad de grabar
        public class Proceso { public int Id; public string Nombre, Ruta; }

        public static List<Proceso> OtrosFastboot(ICollection<int> propios)
        {
            var l = new List<Proceso>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath FROM Win32_Process WHERE Name = 'fastboot.exe' OR Name = 'adb.exe'"))
                using (var r = s.Get())
                    foreach (ManagementObject o in r)
                        using (o)
                        {
                            int id = Convert.ToInt32(o["ProcessId"]);
                            if (propios.Contains(id)) continue;
                            l.Add(new Proceso { Id = id, Nombre = Convert.ToString(o["Name"]), Ruta = Convert.ToString(o["ExecutablePath"]) });
                        }
            }
            catch (ManagementException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            return l;
        }
    }
}
