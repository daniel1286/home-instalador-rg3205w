#!/bin/bash
# Bloquea TLS (port 443) durante 4 segundos tras DHCP lease
# para dar tiempo al NTP de sincronizar el reloj del dispositivo.
# Llamado por dnsmasq: dhcp-script=/opt/mh-fake/dhcp-tls-gate.sh
# Args: add|old|del MAC IP hostname

ACTION=$1
MAC=$2
IP=$3
HOST=$4

LOG="/var/log/tls-gate.log"

case "$ACTION" in
    add|old)
        echo "$(date +%H:%M:%S) GATE ON: $IP ($HOST) - bloqueando 443 por 4s" >> $LOG
        iptables -I INPUT -s "$IP" -p tcp --dport 443 -j DROP 2>/dev/null
        # Abrir despues de 4 segundos (tiempo para NTP sync)
        ( sleep 4
          iptables -D INPUT -s "$IP" -p tcp --dport 443 -j DROP 2>/dev/null
          echo "$(date +%H:%M:%S) GATE OFF: $IP ($HOST) - 443 abierto" >> $LOG
        ) &
        ;;
esac
