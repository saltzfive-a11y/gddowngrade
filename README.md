# GD Downgrade 2.2081

App de Windows que hace sola los pasos de la [guía de beatacvr](https://beatacvr.github.io/downgrade/win.html) para pasar Geometry Dash (Steam) a la versión **2.2081**.

Es una ventana pequeña hecha con [WPF UI](https://github.com/lepoco/wpfui): el texto de lo que está haciendo, una barra de progreso y el botón **Downgrade a 2.2081**.

## Qué hace al pulsar el botón

1. Si Geometry Dash está abierto, espera a que lo cierres.
2. Respalda tu partida (`%LOCALAPPDATA%\GeometryDash\*.dat`) en `Documentos\GD Downgrade\Respaldos`.
3. Abre la consola de Steam, escribe `download_depot 322170 322171 3816559102876907245` y pulsa Enter.
   Si ya descargaste esa versión antes, usa esa descarga.
4. Copia los archivos descargados a la carpeta del juego.
5. Marca `appmanifest_322170.acf` como solo lectura para que Steam no lo actualice.

Si algo falla, el detalle queda en `%LOCALAPPDATA%\GDDowngrade\registro.txt`.

Para volver a 2.2091: quita el "solo lectura" de `appmanifest_322170.acf` y en Steam usa *Propiedades → Archivos instalados → Verificar integridad de los archivos del juego*.

## Compilar

No hace falta instalar nada: `build.bat` usa el compilador de C# que viene con Windows (.NET Framework 4.x). WPF UI y sus dependencias están en `lib` y quedan incrustadas dentro del `.exe`.

```bat
build.bat
```

`GDDowngrade.exe --diag informe.txt` escribe lo que la app detecta (rutas, bloqueo, descargas en el log) sin abrir la ventana.

WPF UI es de lepo.co y tiene licencia MIT (`lib\WPF-UI-LICENSE.md`).
