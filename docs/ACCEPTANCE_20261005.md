# Aceptación v0.9.1 — 2026-10-05

## Resultado

Instalación y arranque automático verificados en Civil 3D 2025. Servidor registrado en Codex mediante su CLI oficial, con respaldo y comparación de la configuración anterior. Contrato `171049b89c39afc5120da6ff`: 28 herramientas y 163 acciones.

| Ensayo sobre archivos nuevos | Resultado |
|---|---|
| Corredor con ensamblaje oficial importado | 64 comprobaciones pasan: targets, geometría XYZ, seis códigos de cantidades estimadas, split/merge y conservación de definiciones |
| Viewport vinculado y refresco explícito | 22 comprobaciones finales pasan, incluida consulta posterior del viewport; guardado/reapertura confirma bloqueo y encendido |
| CSV COGO editable | Exportación, edición, aplicación y relectura; archivo cambiado tras ensayo y origen cambiado son rechazados |
| Comparación construido/diseñado | Desviación y RMSE 0,02 m; cobertura parcial 50 %, un fallo de tolerancia y una muestra fuera de dominio correctamente informados |
| Copia DWG | 8/8 controles: estructura, GUID, nombre, SHA y estado de guardado del origen conservados; sobrescritura rechazada |
| DWG reabierto en lectura | Elevaciones 100/101/102 m y construido +0,02 m conservados en tres controles independientes |
| Civil → Revit 2025.4 | Toposolid editable y malla DirectShape colocados y guardados; controles compartidos con giro de 37° y consulta tipada posterior |
| Revit desde plantilla vacía | Receptor final aplicado, elemento existente confirmado y proyecto guardado; cero advertencias nuevas |
| UNDO | Superficie de ensayo eliminada y ausencia confirmada por consulta nueva; API informa verificación parcial de contenedores modificados |

## Correcciones que exigieron las pruebas

- Registro de confianza limitado al directorio del plugin instalado por año. Respaldo, comprobación de cambios concurrentes, relectura y reversión; `SECURELOAD` conservado.
- Exportación DWG completa, sin el descarte de bloques no referenciados que producía Wblock. Lectura compartida del hash del origen abierto; Push/Pop de DBMOD abarca el commit.
- Getters de targets respetan tipo y cantidad; valores inaccesibles devuelven `null` y su motivo.
- Activación temporal del layout por API para encender el viewport; layout y TileMode originales se restauran y verifican.
- Dirección de transformación Civil → Revit contrastada con controles nativos. Tolerancia explícita para la precisión de almacenamiento de la malla, evidencia estructurada e IDs enteros; duplicados rechazados.

La malla almacenada presentó residual máximo de **0,002994 mm** con giro y **0,002712 mm** en el proyecto vacío, bajo tolerancia explícita de **0,01 mm**. La triangulación fuente conserva sus coordenadas originales; no se afirma precisión numérica infinita en Revit.

## Pruebas y compatibilidad

- 552 pruebas Core y 391 pruebas en cada runtime: net48, net8 y net10.
- 14 pruebas del receptor Python; pruebas de preparación del paquete Revit y registro de confianza en PowerShell 5.1/7.
- Gate de ZIP/plugin/MCPB, hashes y rechazo de tres manifiestos de carga defectuosos.
- Compilaciones completas: Civil 2024/net48, 2025/net8 y 2026/net8/net10. En 2024 permanecen cinco avisos de nulabilidad, sin errores.
- Civil 2024 y 2026 no están instalados en este equipo: su evidencia es **B/T**, no **L**. 2026.2.2+ usa el paquete net10.

## Límites materiales

Las cantidades de corredor son integración estimada por secciones, no el cómputo nativo certificado de materiales. El refresco de planos es explícito. Toposolid puede retriangular; su espesor y volumen no se transfieren del TIN. DirectShape verifica triángulos y posiciones con tolerancia, sin certificar identidad de vértices coincidentes ni winding. La relación CRS debe establecerse con controles del proyecto real; los ensayos usan coordenadas conocidas.

`undo_last` sigue declarando verificación incompleta de valores de contenedores modificados. La consulta independiente demuestra la eliminación de la superficie creada, no una restauración genérica completa.

Dos arranques de ensayo anteriores al registro de confianza quedaron sin puente. No se cerraron por pantalla ni se terminaron por fuerza. La instalación operativa usa una generación de binarios independiente y verificada, sin sobrescribir DLL cargadas. El instalador estándar sigue requiriendo cerrar Civil antes de reemplazar el bundle.

## Evidencia local

Todo está en `.local/acceptance-20261005-221756/`, excluido de Git:

- `engineering-trusted091/engineering-evidence.json`: 64 comprobaciones de corredores y el fallo de viewport que se corrigió después.
- `engineering-sheets-final091/engineering-evidence.json`: 22 comprobaciones finales.
- `export-final-091-20261005-230348/evidence.json` y `copy-readonly-evidence.json`: aceptación final DWG.
- `interop-results.json`: CSV y terreno; conserva el fallo histórico DWG de v0.9.0, sustituido por el ensayo final anterior.
- `revit-results.json`, `revit-clean-results.json`, `final-readback.json`: controles y relecturas.
- `CivilEngineeringSheetsFinal091.dwg`, `RevitTerrainFixture.rvt` y `RevitTerrainClean091.rvt`: archivos de ensayo guardados.

Los ZIP, plugin ZIP, MCPB y `SHA256SUMS` están en `artifacts/`. Son artefactos locales; no se hizo commit, push ni publicación en GitHub.

## Qué sigue

Usar la instalación verificada para los flujos documentados. La siguiente aceptación pendiente es ejecutar el mismo fixture en anfitriones 2024 y 2026 reales; después, ampliar snapshots de UNDO de modificaciones. No se requieren más datos para cerrar este bloque.
