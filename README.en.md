<p align="center">
  <img src="recursos/icono.png" width="96" alt="Instalar Home">
</p>

<h1 align="center">Instalar Home</h1>

<p align="center">
  Take your Askey RG3205W from stock to <a href="https://github.com/daniel1286/home-rom-rg3205w">Home</a> · LineageOS 22 · Android 15
</p>

<p align="center">
  <a href="https://github.com/daniel1286/home-instalador-rg3205w/releases/latest"><img src="https://img.shields.io/github/v/release/daniel1286/home-instalador-rg3205w?label=version&cacheSeconds=3600&style=flat-square&labelColor=1B1A18&color=E0A85A" alt="Version"></a>
  <img src="https://img.shields.io/badge/license-Apache--2.0-E0A85A?style=flat-square&labelColor=1B1A18" alt="License Apache-2.0">
  <img src="https://img.shields.io/badge/Android_15-E0A85A?style=flat-square&labelColor=1B1A18&logo=android&logoColor=E0A85A" alt="Android 15">
  <img src="https://img.shields.io/badge/RG3205W-E0A85A?style=flat-square&labelColor=1B1A18" alt="RG3205W">
</p>

<p align="center">
  <a href="#usb-cable-windows">🔌 USB cable</a>
  &nbsp;·&nbsp;
  <a href="#wireless-raspberry-pi">📡 Wireless</a>
  &nbsp;·&nbsp;
  <a href="https://github.com/daniel1286/home-rom-rg3205w">Home ROM</a>
  &nbsp;·&nbsp;
  <a href="README.md">Español</a>
</p>

---

Two ways to install Home. Pick the one that works for you:

| | 🔌 USB cable | 📡 Wireless |
|---|---|---|
| **You need** | Windows PC + USB cable | Raspberry Pi 3B+ |
| **Touch the device** | Yes (open + USB) | No (everything over WiFi) |
| **Difficulty** | Easy | Medium |
| **Time** | ~15 min | ~25 min (Pi) + ~10 min |

> [!WARNING]
> Installing Home wipes all data on the device. Changing the software may void the warranty.

> [!NOTE]
> The installer window and the Pi control panel are in Spanish. The steps below explain everything in English.

---

# USB cable (Windows)

A single `.exe` that does everything: downloads Home, checks integrity, sets up Windows and flashes it.

<p align="center">
  <img src="recursos/capturas/4b_instalando.png" width="720" alt="Installing Home via USB cable">
</p>

### 📥 Steps

1. **Download** → [Instalar-Home.exe](https://github.com/daniel1286/home-instalador-rg3205w/releases/latest/download/Instalar-Home.exe)
2. **Open** it — if Windows says "protected your PC", click _More info → Run anyway_
3. **Follow** the steps in the window

> [!TIP]
> Already have the Home zip? **Opciones → Elegir zip** to use it without downloading again.

<details>
<summary>📸 <b>Full process screenshots</b></summary>
<br>

<table>
  <tr>
    <td width="50%"><img src="recursos/capturas/1_descargar.png" alt="Step 1: download Home"></td>
    <td width="50%"><img src="recursos/capturas/2_conectar.png" alt="Step 2: connect in fastboot"></td>
  </tr>
  <tr>
    <td><b>1. Download</b> (<i>Descargar</i>). Finds the latest version, downloads it and checks SHA256.</td>
    <td><b>2. Connect</b> (<i>Conectar</i>). Auto-detects the Askey in fastboot mode.</td>
  </tr>
  <tr>
    <td><img src="recursos/capturas/2b_driver.png" alt="USB driver"></td>
    <td><img src="recursos/capturas/3_desbloquear.png" alt="Unlock bootloader"></td>
  </tr>
  <tr>
    <td><b>USB driver.</b> Downloads and installs it in one click if missing.</td>
    <td><b>3. Unlock</b> (<i>Desbloquear</i>). Unlocks the bootloader if needed.</td>
  </tr>
  <tr>
    <td><img src="recursos/capturas/4_instalar.png" alt="Install Home"></td>
    <td><img src="recursos/capturas/5_hecho.png" alt="Done"></td>
  </tr>
  <tr>
    <td><b>4. Install</b> (<i>Instalar</i>). Flashes each image with progress.</td>
    <td><b>Done.</b> Reboot and the Home setup wizard appears.</td>
  </tr>
</table>

<br>

The guide for opening the device, making the cable and entering fastboot is built into the installer:

<p align="center">
  <img src="recursos/capturas/guia.png" width="720" alt="Built-in guide">
</p>

<p align="center">
  <img src="recursos/capturas/opciones.png" width="720" alt="Options">
</p>

</details>

<details>
<summary>📋 <b>Requirements</b></summary>
<br>

- 64-bit Windows 10/11 with .NET Framework 4.8 (ships with Windows)
- Internet to download Home, fastboot and the driver · ~4 GB free
- Askey RG3205W (Rev4 or Rev5) + cable ([which one](https://github.com/daniel1286/home-rom-rg3205w/blob/main/guias/INSTALL.md#2-rev4-or-rev5-and-connecting-it-to-the-pc))

</details>

<details>
<summary>🔒 <b>What it touches and what it doesn't</b></summary>
<br>

- Only flashes `boot`, `recovery`, `vendor`, `system` and `splash`, only formats `cache` and `userdata`
- Checks the device is an RG3205W, still the same one and still unlocked
- Won't flash if SHA256 or size doesn't match
- Asks Home's server which versions exist, sending nothing · the zip downloads from GitHub Releases through Home's server (which only counts it with the box checked) and its SHA256 is checked
- Never restarts the Askey or relocks the bootloader
- Stores its files in `%LOCALAPPDATA%\Home\Instalador` · Home in `Downloads\Home`

</details>

<details>
<summary>📊 <b>Count this installation</b> (<i>Contar esta instalación</i>)</summary>
<br>

The box comes checked. When it finishes, the installer tells the Home statistics whether the installation went well or which step failed. Exactly this:

```json
{"v":1,"camino":"cable","instalador":"2.1","home":"2.1","resultado":"ok","duracion_s":840}
{"v":1,"camino":"cable","instalador":"2.1","home":"2.1","resultado":"fallo","paso":"grabar_system","duracion_s":312}
```

- No ID, no name, nothing about the PC or the Askey · the IP address is not stored
- Only sent when an installation ends, or when the download, driver or unlock fails
- The Home download goes through Home's server on its way to GitHub: checked, it is counted (day, version and that it comes from the installer); unchecked, it is not
- Can be unchecked before installing and is remembered on this PC · it stays fixed while installing
- If there is no Internet or the server doesn't answer, nothing happens: the installation carries on
- **Qué se envía** (*What is sent*), under the box, shows this same text

</details>

<details>
<summary>🩺 <b>Failure report</b> (<i>Informe del fallo</i>)</summary>
<br>

Separate from the box and never sent on its own. If something fails, the installer offers **Enviar informe del fallo** (*Send failure report*): it shows the exact log that would be sent and only sends it if you press **Enviar informe**.

- Cleaned before it is shown: no Askey serial number, no user or PC name, no email, IP or MAC addresses
- Only the end of the log, where the failure is (about 11,500 characters at most)
- Returns a code such as `H-7K3Q2M`: put it in your issue and the log can be read without attaching anything
- Kept for 90 days and only seen by whoever maintains Home

</details>

<details>
<summary>❓ <b>FAQ</b></summary>
<br>

**Why does "Windows protected your PC" show up?**
Windows shows it for unsigned, rarely downloaded programs. Check SHA256 from each [release](https://github.com/daniel1286/home-instalador-rg3205w/releases/latest):
```powershell
Get-FileHash .\Instalar-Home.exe -Algorithm SHA256
```

**Do I need to run it as administrator?**
No. It only asks for elevation to install the USB driver.

**How do I remove it?**
Delete `Instalar-Home.exe` and `%LOCALAPPDATA%\Home\Instalador`.

**Something went wrong.**
Open an [issue](https://github.com/daniel1286/home-instalador-rg3205w/issues/new/choose) with the code from **Enviar informe del fallo** or, if you'd rather not send it, with the log from **Opciones → Registros**.

**Can I do it manually?**
Yes → [manual guide](https://github.com/daniel1286/home-rom-rg3205w/blob/main/guias/INSTALL.md).

</details>

<details>
<summary>🔨 <b>Building</b></summary>
<br>

With the C# compiler shipped with Windows (.NET Framework 4.8):

```
compilar.cmd
```

Result: `salida\Instalar-Home.exe`. GitHub Actions builds on every push; a `vX.Y` tag triggers a release (if it already exists, its .exe and SHA256 are replaced).

Demo mode (no device needed):
```
set INSTALADOR_DEMO=instalando
salida\Instalar-Home.exe
```

| File | Purpose |
|---|---|
| `fuente/Programa.cs` | Entry point, single instance |
| `fuente/Ventana.xaml` · `Ventana.cs` | Window and UI |
| `fuente/Pantallas.cs` · `Acciones.cs` | Screens and button actions |
| `fuente/Trabajos.cs` | Download, driver, unlock, flash |
| `fuente/Red.cs` | Version list (Home's server) and resumable downloads |
| `fuente/Imagenes.cs` | Extract and verify images |
| `fuente/Fastboot.cs` | Locate and run fastboot |
| `fuente/Guia.cs` · `Piezas.cs` | Built-in installation guide |
| `recursos/guia/fuentes/` | Sources of the guide drawings (`hacer.ps1` redraws them with Edge) |
| `fuente/Estadistica.cs` | Anonymous installation count and failure report |
| `fuente/Rutas.cs` · `Estado.cs` | Paths and shared state |

</details>

---

# Wireless (Raspberry Pi)

No need to open the device or plug in cables. A Raspberry Pi acts as a local backend: the device connects to its WiFi, receives the update and boots into Home. **Fully automatic.**

<p align="center">
  <img src="recursos/inalambrico/arquitectura.svg" width="720" alt="Network architecture">
</p>

<details>
<summary>🔄 <b>See the step-by-step process</b></summary>
<br>

After a factory reset, the device tries to reach the original servers — but the Pi answers instead: it resolves all domains to itself, serves the APIs the firmware expects and offers the update. The device downloads the full Home ROM, verifies its signature (trusted) and installs it in a single cycle from recovery. When it finishes, one last **factory reset** from recovery leaves Home booted.

<p align="center">
  <img src="recursos/inalambrico/pasos.svg" width="720" alt="Migration process">
</p>

</details>

### ⚙️ Set up the Pi

**1.** Flash Raspberry Pi OS to the SD card and connect the Pi to the router via ethernet

**2.** Clone and install:

```bash
git clone https://github.com/daniel1286/home-instalador-rg3205w.git
cd home-instalador-rg3205w/inalambrico
sudo bash instalar.sh
```

> [!NOTE]
> The script installs packages, sets up the 5 GHz WiFi access point, generates TLS certificates, installs the 9 backend services and downloads the OTA files from Releases.

**3.** Reboot: `sudo reboot` — the backend starts automatically.

### 📱 Migrate the device

| Step | What to do |
|:---:|---|
| **1** | 🔄 Factory reset → _Settings → Backup & Reset → Factory Data Reset_ |
| **2** | 📶 Connect to WiFi → Network: **`Home-migracion`** · Password: **`homeupdate`** |
| **3** | ⏳ Automatic — downloads the OTA, reboots into recovery and installs Home |
| **4** | ✅ **Final step** — the device stays in recovery: tap **_Wipe_ → _Factory reset_**. It then boots into Home (the first boot takes a few minutes) |

<details>
<summary>🖥️ <b>Control panel</b></summary>
<br>

From any browser on the Pi's network:

> **http://10.42.0.1:8080**

Shows active services, connected device, OTA progress and the live log.

</details>

<details>
<summary>🔧 <b>Services and configuration</b></summary>
<br>

9 services that start automatically with the Pi:

| Service | Port | Purpose |
|---|---|---|
| `hostapd` | wlan0 | WiFi 5 GHz AP (channel 36) |
| `dnsmasq` | 53 | DNS → redirects domains to backend + DHCP |
| `mh-sni` | 443 | TLS/SNI → firmware API endpoints (77) |
| `mh-captive` | 80 | Captive portal → returns 204 |
| `mh-ntp` | 123/UDP | NTP → fixed date for cert validity |
| `mh-cwmp` | 7016 | TR-069 TLS → delivers OTA to device |
| `mh-cwmp-http` | 7006 | TR-069 HTTP → fallback |
| `mh-ota` | 8000 | HTTP server for OTA files |
| `mh-panel` | 8080 | Web control panel |

**Configuration** → `/etc/default/mh-fake`:

| Variable | Value | Purpose |
|---|---|---|
| `MH_MODE` | `ota` | Normal mode: delivers the Home OTA |
| `MH_MODE` | `onboarding` | Wizard only, no OTA (for testing) |

Change mode:
```bash
sudo sed -i 's/MH_MODE=.*/MH_MODE=onboarding/' /etc/default/mh-fake
sudo systemctl restart mh-sni mh-cwmp mh-cwmp-http
```

SSID/password → `/etc/hostapd/hostapd.conf` → `sudo systemctl restart hostapd`

</details>

<details>
<summary>📋 <b>Requirements</b></summary>
<br>

- **Raspberry Pi 3B+ or better** with 5 GHz WiFi (the RG3205W only connects on 5 GHz)
- **Raspberry Pi OS** (Lite is fine)
- **Ethernet cable** to the router (only during Pi setup)
- OTA files are downloaded automatically by the install script, or from [Releases](https://github.com/daniel1286/home-instalador-rg3205w/releases)

</details>

<details>
<summary>📂 <b>File structure</b></summary>
<br>

```
inalambrico/
├── instalar.sh                 install script
└── backend/
    ├── sni_server.py           TLS/SNI (77 endpoints)
    ├── cwmp_server.py          TR-069 ACS (delivers OTA in 2 phases)
    ├── captive_server.py       captive portal
    ├── ota_server.py           HTTP server for OTAs
    ├── ntp_server.py           NTP server
    ├── generate_certs.py       certificate generator
    ├── panel_server.py         control panel
    ├── panel.html              panel UI
    ├── run-sni.sh              SNI launcher
    ├── run-cwmp.sh             ACS TLS launcher
    ├── run-cwmp-http.sh        ACS HTTP launcher
    ├── config/
    │   ├── hostapd.conf        WiFi 5 GHz AP
    │   ├── dnsmasq.conf        DNS + DHCP
    │   └── backend.default     environment variables
    └── systemd/                service units
```

On the Pi: `/opt/mh-fake/` · Certs: `/opt/mh-fake/certs/` · OTAs: `/opt/mh-fake/otas/`

</details>

<details>
<summary>❓ <b>FAQ</b></summary>
<br>

**Which Pi models work?**
Any with 5 GHz WiFi: Pi 3B+ (ac), Pi 4, Pi 5. Pi 3B and Pi Zero **don't** (2.4 GHz only).

**Why a final factory reset?**
After installing Home, the device stays in recovery. A factory reset clears the data so Home can boot; it's a single step and the first boot takes a few minutes.

**Do I need internet during migration?**
No. Only during Pi setup. After that it works on the Pi's own local network with no internet.

**Can I migrate multiple devices?**
Yes. Each factory reset + WiFi connection starts the process. The backend resets automatically.

**The device connects but nothing happens.**
Check the 9 services:
```bash
sudo systemctl status mh-sni mh-captive mh-ntp mh-cwmp mh-cwmp-http mh-ota mh-panel hostapd dnsmasq
```
If any fail → `journalctl -u mh-sni -f`

**Can I change the SSID and WiFi password?**
Yes → edit `/etc/hostapd/hostapd.conf` → `sudo systemctl restart hostapd`

</details>

---

<details>
<summary><b>Credits</b></summary>
<br>

- [Inter](https://rsms.me/inter/), by Rasmus Andersson — [SIL Open Font License 1.1](recursos/letras/OFL.txt)
- Board photos: [zry98/movistar-home-hacks](https://github.com/zry98/movistar-home-hacks)
- fastboot and Google USB Driver are downloaded from Google when needed

</details>

**[Apache License 2.0](LICENSE)**

<p align="center">
  <sub>Independent project, not affiliated with Askey or any carrier. All trademarks belong to their respective owners.</sub>
</p>
