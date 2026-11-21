# Changelog

## 1.0.4

- Componente nuevo «Baterías»: muestra el % de batería de ratones, teclados y audífonos inalámbricos con un icono,
  el porcentaje y una barra (colores de aviso por debajo del 30 % y 15 %, rayo al cargar).
  Compatible con Logitech LIGHTSPEED/Unifying/Bolt (HID++) y la base ASTRO A50 Gen 5. No necesita G HUB.
- Opciones del componente: qué dispositivos mostrar, en fila o en columna, nombres y ocultar los apagados.
- «Ver todos los sensores» lista también las baterías detectadas.
- Corrección: el reloj reduce la letra si la hora no cabe (se cortaba en temas como Terminal).

## 1.0.3

- Instalador: si TuringLite está abierto, ofrece cerrarlo y continuar en lugar de detenerse.
- Instalador: si el servicio de sensores ya está instalado, siempre se actualiza (se reinicia con la versión nueva),
  para que no quede en una versión distinta a la de la app. También en la instalación silenciosa.

## 1.0.2

- Sensor nuevo «PC · Potencia total (CPU + GPU)»: suma la potencia del procesador y la de la GPU dedicada
  (la integrada no se suma, su consumo ya va dentro del procesador).
- Opcional en ese sensor: watts extra estimados (placa, RAM, discos, ventiladores) y eficiencia de la fuente
  para aproximar el consumo desde el enchufe. Con estimación, el valor se muestra con «~».
- El editor de componentes es un poco más alto para los nuevos campos.

## 1.0.1

- Editor de componentes: botón «Restablecer por defecto», que vuelve al diseño de fábrica para la orientación actual.
- Editor de componentes: casilla «Bloquear posición y tamaño» por componente, para no moverlo sin querer.
- Ventana principal: la GPU se elige en una lista con las tarjetas detectadas («Automática» elige la dedicada).
- Ventana principal más compacta: sin el subtítulo ni la descripción del tema.

## 1.0.0

Primera versión publicable.

- Instalador de un solo archivo (`TuringLite-Setup-1.0.0.exe`): instalación por usuario, acceso directo, inicio con Windows y servicio de sensores opcional.
- Desinstalador que quita solo lo que instaló: programa, inicio, servicio, acceso directo, entrada de Aplicaciones instaladas y huellas de Windows (historial, caché y Prefetch). Conserva la configuración si lo pides.
- Servicio de sensores `TuringLiteSensors`: lee la CPU con privilegios de sistema y la comparte con la app por una tubería local de solo lectura. La app ya no necesita administrador.
- Temas visuales: Clásico, Gamer · Neón, Gamer · Cyberpunk, Gamer · Fuego, Programador · Terminal, Programador · Nocturno, Minimalista · Blanco, Minimalista · Grafito, Retro · Ámbar, Suave · Cielo y Alto contraste.
- Editor de componentes y ventana de ajustes con tema oscuro.
- Corrección: los valores no legibles (p. ej. temperatura de CPU sin administrador) se muestran como `--`, no como `0`.
- Corrección: configuraciones con valores fuera de rango, formatos de hora no válidos o colores incorrectos ya no detienen la app.
- Corrección: el cambio de idioma de entrada con la ventana abierta ya no provoca un error.
- Corrección: la app ya no pide administrador al arrancar con Windows.
