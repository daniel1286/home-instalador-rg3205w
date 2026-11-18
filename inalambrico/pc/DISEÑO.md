# Instalación inalámbrica desde el PC (sin Raspberry Pi)

Borrador de diseño. El objetivo: que un usuario sin Raspberry Pi pueda migrar la pantalla por WiFi usando solo su
ordenador. El PC hace lo que hoy hace la Pi: crea el WiFi al que se conecta la pantalla y le sirve la OTA de Home.

## La idea en una línea

El programa usa **la tarjeta WiFi del propio PC** como punto de acceso de 5 GHz (la pantalla solo ve 5 GHz), levanta
el mismo backend que ya corre en la Pi (los servidores de `backend/`, que son Python y ya son portables) y, al
terminar o cerrarse, **deja el PC exactamente como estaba**.

## El muro que hay que mirar de frente

No todas las tarjetas WiFi de portátil pueden crear un WiFi de 5 GHz. Esto no lo arregla el código: es la tarjeta. Por
eso lo **primero** que hace el programa es comprobarlo, y si no puede, lo dice claro y manda al método por cable, sin
haber tocado nada.

- **Linux:** fiable si la tarjeta admite modo AP. Se usa `hostapd` + `dnsmasq` + el backend actual. Control total.
- **Windows:** la única vía amplia para el AP de 5 GHz es la API de «Zona con cobertura» (Mobile Hotspot,
  `NetworkOperatorTetheringManager`). El punto delicado es el DNS, y hay una forma limpia de resolverlo (ver abajo).

## Windows: cómo hacerlo limpio y profesional

El problema no es crear el WiFi (eso lo hace Mobile Hotspot en 5 GHz en tarjetas modernas), sino **redirigir los
dominios** sin romper nada. El hotspot de Windows trae su propio DNS compartido (ICS). En vez de pelearse con él,
se usa a favor. De mejor a peor:

1. **Apuntar el DNS del PC a nuestro resolutor (la vía preferida).** Mobile Hotspot crea el AP y reparte él la red.
   La pantalla pregunta los dominios al DNS de Windows (ICS), y ICS reenvía esas preguntas al DNS que tenga puesto el
   PC en su conexión a internet. Así que basta con **poner como DNS del PC nuestro propio servidor local**: responde
   los dominios de la pantalla hacia el backend y deja pasar el resto (por el cable ethernet). No se mata ningún
   servicio de Windows, solo se cambia un ajuste —el DNS del PC— que se restaura al salir. Es lo más reversible.
2. **Red alojada heredada (`hostednetwork`), si la tarjeta aún la trae.** Algunas tarjetas Intel todavía dejan crear
   el AP con control total (nuestro propio DHCP/DNS/NAT, como en Linux). El programa lo detecta y, si está, lo usa:
   es el caso más limpio. Si no está, se va a la vía 1.
3. **Componente de redirección incluido en el .exe (último recurso).** Un redirector de paquetes en modo usuario
   (tipo WinDivert, un controlador firmado que se incluye dentro del propio programa, **nunca hardware**) da control
   total de lo que entra y sale, pase lo que pase con el DNS. Es más pesado (controlador con firma, admin, algún
   antivirus puede mirarlo con lupa), así que solo si las vías 1 y 2 no bastan.

**Las tres se validan en un prototipo pequeño antes de prometer la versión de Windows.** La vía 1 es la apuesta: si
funciona, Windows queda «sin tocar nada raro» y totalmente reversible.

## Flujo para el usuario

1. **Comprobación.** Al abrir, el programa mira si la WiFi del PC puede crear el punto de acceso de 5 GHz. Si no
   puede: «Tu WiFi no puede hacer esto; usa el método por cable», y se cierra sin cambiar nada.
2. **Aviso del internet y el cable.** Antes de tocar la WiFi:
   > «Para la migración, la WiFi de este PC se convertirá en el WiFi de la pantalla. **Mientras dure, el PC se quedará
   > sin internet por WiFi.** Conecta un cable de red (ethernet) al PC para que siga teniendo internet.»
   El programa comprueba si hay ethernet con internet y lo enseña en verde/ámbar. Si falta, no bloquea, pero avisa.
   (La OTA ya está descargada en el PC antes de empezar, así que la migración en sí no necesita internet; el cable es
   para que el usuario no se quede aislado y para descargar la OTA la primera vez.)
3. **Montaje.** El programa guarda el estado actual de la red (ver «Volver atrás»), crea el punto de acceso
   `Home-migracion` / `homeupdate`, levanta el backend y enseña un panel dentro de su propia ventana: esperando
   pantalla, progreso de la OTA y registro, como el panel de la Pi pero integrado.
4. **Migración.** El usuario hace restablecimiento de fábrica en la pantalla, la conecta a `Home-migracion`, y la OTA
   fluye igual que con la Pi.
5. **Fin.** Al terminar, o al cerrar la ventana, o si el programa se cierra de golpe: **todo vuelve a como estaba**.

## Volver atrás siempre (lo que lo hace profesional)

Regla de oro: **cambiar lo mínimo del sistema**, y que cada cambio sea reversible. Y apoyarse en que el propio sistema
deshace cosas (al apagar el hotspot de Windows, la WiFi se reconecta sola a la red de antes).

Mecanismo:

- **Antes de tocar nada**, el programa escribe un fichero de «restauración pendiente» con la foto del estado: a qué
  WiFi estaba conectado el PC, el DNS, las reglas de cortafuegos que va a añadir, el estado del adaptador.
- **Cada cambio se registra** según se hace.
- **Al cerrar bien** (botón, cerrar ventana, Ctrl+C): deshace todo en orden y borra el fichero.
- **Si se cierra de golpe** (cuelgue, se mata el proceso, se va la luz): como un proceso muerto no limpia solo, al
  **volver a abrir** el programa detecta el fichero de restauración pendiente y deshace todo **antes de hacer nada
  más**. Primera prioridad al arrancar.
- Nada de cambios que el usuario tenga que revertir a mano. Si algo no se puede deshacer solo, no se hace.

Qué se restaura: el punto de acceso se apaga, el backend se para, se quitan las reglas de cortafuegos y las rutas que
se añadieron, el DNS vuelve a su sitio y el PC se reconecta a su WiFi de siempre.

## Qué se reutiliza y qué es nuevo

| Pieza | Estado |
|---|---|
| Servidores del backend (`backend/*.py`) | **Se reutilizan tal cual.** Ya son Python y portables |
| Certificados, OTA, lógica de la migración | **Igual que en la Pi** |
| `instalar.sh` (monta la Pi) | **No se usa** en el PC: el programa lo hace todo en memoria y se limpia al salir |
| Crear el punto de acceso de 5 GHz | **Nuevo**, por sistema: `hostapd` en Linux, API de Mobile Hotspot en Windows |
| DHCP/DNS | Linux: `dnsmasq`. Windows: a resolver en el prototipo (el punto delicado) |
| Guardar estado y restaurar | **Nuevo**, el corazón de «sin problemas» |
| Panel dentro de la ventana | **Nuevo**: el panel de la Pi, integrado en la app |

## Dónde encaja en el instalador

El instalador por cable ya es una app de escritorio (`fuente/`, .NET/WPF para Windows). Dos caminos:

- **Windows:** añadir esta vía como una opción más dentro de la app que ya existe. Una pantalla: «Instalar por WiFi
  (sin cable)», con la comprobación, el aviso del ethernet, el panel y el botón de parar.
- **Linux:** un programa aparte equivalente, ya que la app WPF es de Windows. Comparte el backend.

## Lo primero que hay que probar (antes de construir)

1. **Prototipo de Windows:** crear el AP de 5 GHz con la API de Mobile Hotspot y conseguir que la pantalla resuelva
   los dominios hacia nuestro backend a pesar del DNS propio de Windows. **Esto decide si la versión de Windows es
   viable sin añadir nada al sistema.** Si no se puede doblar ese DNS, hay que decidir entre otra técnica o aceptar que
   Windows necesita una pieza extra incluida en el propio .exe (nunca hardware).
2. **Linux:** confirmar con una tarjeta normal de portátil que el AP de 5 GHz + backend + restauración funcionan de
   principio a fin.

Hasta que el punto 1 esté probado, no se promete una versión de Windows «sin problemas»: se prueba y luego se promete.

## Red de seguridad: imagen de Raspberry Pi lista para usar

Si para alguna tarjeta WiFi no hay forma limpia, queda la imagen de Pi: se graba en la tarjeta y arranca haciéndolo
todo sola, sin montar nada. Una aclaración honesta sobre «encriptada para que no vean el proceso»: una tarjeta que
tiene que arrancar sola no se puede cifrar de verdad contra quien la tenga en la mano (la llave tendría que estar en
la propia tarjeta, así que se podría leer). Lo que sí se hace, y queda profesional, es un **aparato cerrado**: sistema
de solo lectura, sin usuario ni acceso, arranca directo a la tarea y el código va empaquetado, no a la vista. El
usuario no ve el proceso y no puede tocarlo; es «cerrado», no «cifrado». Lo prometemos como lo que es.
