# EMT Package Manager (`com.emt.package-manager`)

Herramienta **solo para el Unity Editor** que descubre, comprueba y actualiza todos los paquetes propios `com.emt.*` instalados en el proyecto, usando **GitHub Releases** y **SemVer**.

## Instalación

`Window > Package Manager > + > Add package from git URL...`

```
https://github.com/DonMario-Git/EMTPackageManager.git#v1.0.0
```

(ajusta la URL a tu repositorio real y actualiza `repository.url` en `package.json`).

Después abre **EMT > Package Manager**.

## Cómo funciona

```
Client.List() → filtro com.emt.* → repository.url (package.json) → GitHub API /releases/latest → SemVer → estado
```

- No hay ninguna lista de paquetes: cualquier `com.emt.nuevo-paquete` aparece solo (la ventana se refresca cuando Unity registra paquetes).
- Los paquetes locales (`file:` o embebidos en `/Packages`) se marcan **Local Development** y nunca se consultan ni actualizan.
- Paquetes sin `repository.url`, de registry o built-in → **No Repository**.
- Varios paquetes del mismo repositorio comparten una única consulta a GitHub.

## Requisitos para tus paquetes `com.emt.*`

1. `package.json` con `name`, `version` (SemVer) y `repository.url` (GitHub).
2. Publicar una **GitHub Release** (no solo un tag) cuyo tag sea SemVer (`v1.3.0` o `1.3.0`). Se usa `releases/latest`: ignora drafts y pre-releases.
3. El **tag de la release se usa como ref de Git** en `manifest.json`: `...EMTCore.git#v1.3.0`. El tag debe coincidir con la `version` del `package.json` de ese commit.
4. Dependencias declaradas en `Packages/manifest.json` como URL Git (para actualizar).

## Estados

`UpToDate`, `UpdateAvailable`, `MajorUpdateAvailable`, `NoRepository`, `LocalDevelopment`, `CheckFailed`, `InvalidVersion` (+ `NotChecked` antes de la primera comprobación).

## Comprobación automática y caché

- Una sola comprobación por sesión del Editor, y como máximo una cada N horas (por defecto 24; opciones 6 h / 12 h / 24 h / 3 d / 7 d). Se guarda en `EditorPrefs`, por proyecto.
- Caché JSON en `Library/EMTPackageManager/update-cache.json` (expira con el intervalo; solo guarda comprobaciones exitosas). **Check for Updates** la ignora.
- Si "Check automatically" está desactivado, abrir la ventana solo usa la caché, sin red.

## Actualización

- Solo cambia el `#ref` de esa dependencia en `Packages/manifest.json` (reemplazo textual dirigido; el resto del archivo queda intacto) y llama a `Client.Resolve()`.
- Antes de escribir se valida: tag SemVer válido y seguro, dependencia Git, mismo repositorio que `package.json`, y que no estemos en Play Mode.
- Copia de seguridad previa en `Library/EMTPackageManager/manifest.backup.json`.
- Confirmación siempre; las actualizaciones MAJOR muestran advertencia y **Update All** pide confirmación adicional para ellas.

## Registro por defecto (opcional)

`ProjectSettings/EMTPackageRegistry.json` lista los paquetes que quieres tener disponibles. Se crea desde **Settings > Create File** en la ventana:

```json
{ "packages": [ { "name": "com.emt.core", "repository": "https://github.com/DonMario-Git/EMTCore.git" } ] }
```

Cada entrada muestra su estado en Settings: `Installed`, `Available` (con botón **Install**, con confirmación), `Unavailable` o `Invalid`. Es solo datos: los paquetes instalados se siguen descubriendo con `Client.List()`.

## Seguridad

Solo consulta metadata (`GET /repos/{owner}/{repo}/releases/latest`). No descarga ni ejecuta nada de GitHub. Las peticiones son asíncronas (timeout 15 s). El token de GitHub es opcional (repos privados / rate limit), usa uno de solo lectura: `EditorPrefs` lo guarda sin cifrar.

## Tests

Añade el paquete a `testables` en `Packages/manifest.json` del proyecto y abre **Window > General > Test Runner > EditMode**:

```json
"testables": [ "com.emt.package-manager" ]
```
