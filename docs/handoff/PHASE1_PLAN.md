# PHASE 1 PLAN: superficies (`horizun_c3d_surface` y relacionadas)

> Objetivo: traer al MCP de Horizun **todo lo que ya se validó en producción** con el conector parcheado, reescrito
> con el contrato Horizun (dry run, token, commit, re-lectura) y **con nombres Horizun**.
> Fuente a portar (leer, NO copiar nombres): el conector anterior (retirado, archivado fuera del repo).
> Hallazgos de API ya verificados: `docs/API_NOTES_CIVIL3D_2025.md` y `docs/api-probes/2025/`.

## 0. Diseño de la herramienta

Un solo tool con acciones. El catálogo debe ser pequeño: la meta es tener menos de 30 tools en total.

```
horizun_c3d_surface  action = list | get | volumes_report | sample_elevation            (Read)
                              rename | set_style | duplicate_style | style_display        (SafeWrite)
                              apply_elevation_analysis | apply_slope_analysis             (SafeWrite)
                              create_tin | add_data | paste | create_volume | rebuild     (SafeWrite)
```

- Declararlo en `src/Horizun.Civil3D.Core/Contract.cs` con `ActionEffects` por acción. Las lecturas van como `Read` y las escrituras como `SafeWrite`.
- Implementarlo en `src/Horizun.Civil3D.Plugin/Commands/SurfaceCommand.cs`, separado en clases internas por grupo si crece.
- Todas las acciones aceptan `target_document`, que es obligatorio en las escrituras.
- Varias superficies por llamada: `names: [...]`, con resolución previa. Si cualquier nombre falla, se rechaza toda la llamada.

## 1. Lecturas

| Acción | Qué hace | Base existente (solo referencia) | API verificada |
|---|---|---|---|
| `list` / `get` | Lo mismo que `query` más estadísticas de isopaca: corte máx/mín, relleno máx, **medias ponderadas por área** (V/A), áreas de corte y de relleno, mediana y P90 | `SurfaceManageCommands.GetSurfaceInfoAsync` / `BuildInfo` / `SampleBands`; `Indicaciones/scripts/python/isopaca_stats.py` | `GetGeneralProperties`, muestreo `FindElevationAtXY` |
| `volumes_report` | Corte, relleno y neto de una `TinVolumeSurface`, más el área. Con `base` y `comparison` sin crear objeto. Opcionalmente dentro de una polilínea cerrada (`GetBoundedVolumes`). Factores de corte y relleno | `SurfaceCommands.ComputeSurfaceVolumeAsync` | `TinVolumeSurface.GetVolumeProperties()`, que **exige una transacción de escritura**: abrir Write y hacer **Abort**, nunca Commit. Propiedades `Unadjusted/Adjusted Cut/Fill/NetVolume` |
| `sample_elevation` | Cota en XY, en una lista de puntos o a lo largo de una línea con paso | `SurfaceCommands.SampleSurfaceElevationsAsync`, `GetSurfaceElevationsAlongAsync` | `FindElevationAtXY`, que lanza una excepción fuera de la superficie. Ese caso devuelve `null`, **nunca 0** |

> El código viejo hace `?? 0d` en los volúmenes. **NO portar ese patrón**: un valor ausente es `null` con motivo.

Validación de referencia: en NCA, la isopaca TORRE1 dio corte 1371.3 m³ y relleno 398.1 m³, y el muestreo cuadró con el volumen de Civil (diferencia menor al 0.1 %). Usa `Reconcile.Compare` para reportar volumen de Civil 3D frente a volumen muestreado.

## 2. Escrituras simples

| Acción | Notas | API |
|---|---|---|
| `rename` | Re-lee `Name` | `Entity.Name` set |
| `set_style` | Resuelve el estilo antes de abrir la transacción. **El dry run lista las demás superficies que usan el estilo de destino** (usar `StylesCommand.Usage`). Re-lee `StyleId` y `StyleName` | `Surface.StyleId` |
| `duplicate_style` | `CopyAsSibling(nuevo)`; rechazar si el nombre ya existe. Re-lee que el estilo nuevo exista | `StyleBase.CopyAsSibling` |
| `style_display` | Visibilidad, color y capa de componentes (Points, Triangles, Border, Major/Minor contour, Elevations, Slopes...) en plano o en modelo. **Si el estilo lo comparten otras superficies, advertirlo en el dry run y sugerir `duplicate_style`** | `SurfaceStyle.GetDisplayStylePlan/Model(SurfaceDisplayStyleType)` |

## 3. Análisis

| Acción | Modos y parámetros | Verificación |
|---|---|---|
| `apply_elevation_analysis` | `equal` (N rangos), `step` (paso + `break_at`), `ranges` (lista explícita `[{min,max,color}]`), `recolor` (solo colores). `color_scheme` o colores explícitos (RGB `#RRGGBB` o ACI). La **leyenda llega como parámetro**: la leyenda estándar de isopacas de Horizun es un *preset del cliente* (ver `Indicaciones/datos/leyenda_completa.json`) y **no se compila** | Re-leer `Analysis.GetElevationData()`: rangos y colores aplicados. Reportar el área y el volumen real por banda (muestreo) y comprobar que la suma de las bandas ≈ volumen de Civil 3D (±0.5 %) |
| `apply_slope_analysis` | Rangos en %, `[{min,max,color}]` o N iguales | Re-leer `GetSlopeData()`, área por rango |

Base: `SurfaceManageCommands.SetElevationAnalysisAsync`, `GetElevationAnalysisAsync`, `EqualRanges`, `StepRanges`, `BuildPalette`, `ParseColor`. **La herramienta vieja `analyze_elevation` del upstream es FALSA** (inventa áreas iguales): no tomarla como referencia.

## 4. Construcción

| Acción | Notas | API verificada |
|---|---|---|
| `create_tin` | Nombre único, estilo, capa y descripción | `TinSurface.Create(name, styleId)` |
| `add_data` | Puntos, breaklines estándar desde Polyline3d o **FeatureLine** (dinámicas), bordes outer, hide o show (no destructivos), DEM. Reportar puntos y triángulos antes y después | `AddVertices(Point3dCollection)`, `BreaklinesDefinition.AddStandardBreaklines(ids, midOrd, maxDist, weedDist, weedAngle)`, `BoundariesDefinition.AddBoundaries(ids, midOrd, SurfaceBoundaryType, nonDestructive)` |
| `paste` | Pega en orden en un destino | `TinSurface.PasteSurface(ObjectId)` |
| `create_volume` | `TinVolumeSurface` entre base y comparación, con estilo. Re-lee corte, relleno y neto **desde el objeto creado**. La comparación es de solo lectura: para cambiarla hay que borrar y recrear | `TinVolumeSurface.Create(name, baseId, compId, styleId)` |
| `rebuild` | Explícito; re-lee `IsOutOfDate` | `Surface.Rebuild()` |

## 5. Escotilla de escape: `horizun_c3d_execute_csharp`

- Port de `ScriptCommands.ExecuteScriptAsync`, que es Roslyn. Las variables globales son doc, civilDoc, db, tr y args.
- Necesita el paquete `Microsoft.CodeAnalysis.CSharp.Scripting`, versión 4.8.0 en el conector viejo, y sus DLL en el bundle.
- Efecto `UnsafeCode`. Requiere `permission_profile=unsafe_code` más `enable_execute_csharp=true`, y la **aprobación del dueño en un diálogo dentro de Civil 3D**.
- Modo `query`: transacción siempre abortada. Las respuestas se marcan `self_reported` y `host_verified=false`.

## 6. Fixture y verificación en vivo

- Comando `HZ_BUILD_FIXTURE`: crea un DWG determinista con estos elementos:
  - EG: una malla de puntos con un relieve conocido, por ejemplo un plano inclinado o un paraboloide;
  - FG: una plataforma a cota fija;
  - un borde;
  - un alineamiento.
- Los volúmenes esperados se calculan **analíticamente** y se comparan con tolerancia.
- `scripts/verify-live.ps1`: ejecuta cada acción en dry run y aplicada, los rechazos esperados y la cola, contra el fixture, usando `scripts/mcp_call.py` o el mismo exe.

## 7. Caso de aceptación de la fase 1

> "Crea una superficie de volumen entre EG y FG, dime corte, relleno y neto, aplica un análisis de pendientes a FG con
> rangos 0-2, 2-5, 5-10, 10-30 y >30 %, y cambia FG a un estilo que muestre pendientes."

Este caso debe pasar en vivo, en una sola conversación, con verificación de cada paso.

## 8. Fases siguientes (resumen; detalle en `docs/ROADMAP.md`)

- **Fase 2:**
  - feature lines;
  - **motor de grading geométrico**, port de `Civil3D-MCP-Plugin/Scripts/grading_engine.csx`, validado contra un grading nativo con dz mediana de 0.000 m y volumen −0.09 %. Se etiqueta como geométrico, no nativo;
  - alineamientos y perfiles;
  - corredores;
  - `execute_plan`.
- **Fase 3:**
  - redes;
  - secciones;
  - puntos COGO;
  - data shortcuts;
  - exportación;
  - procedimientos de movimiento de tierras (zona completa, terreno final solo con breaklines de implantación, conformación con fila de control; ver `Indicaciones/04_FLUJOS_DE_TRABAJO.md`);
  - Excel y Power BI.
- **Gradings nativos**: no existe API pública. Se rechazan por nombre y se ofrece el motor geométrico.


## Avance 2026-10-01 — primer bloque instalado v0.2.0

Implementadas diez acciones: list, get, volumes_report, sample_elevation, rename, set_style, duplicate_style, create_tin, create_volume y rebuild. Guía/limitaciones: docs/SURFACES.md. Contrato 176bfc22a49d728636cc039a, catálogo siete tools, 119 pruebas locales y build 2025 limpio. Instalado y verificado por hashes; **ninguna acción nueva tiene todavía L**.

El diseño anterior es la meta completa, no una lista de capacidades disponibles. Aún NO están expuestos bounded volumes por polígono, style_display, análisis, add_data, paste, execute_csharp ni fixture. create_tin produce una TIN vacía. Las áreas/profundidades muestreadas son estimaciones explícitas; no confundirlas con mediciones exactas por banda.

Siguiente orden: add_data (puntos/breaklines/límites) + paste; fixture determinista y regresión en vivo autorizada; análisis/visualización; feature lines. La configuración de cuenta ChatGPT se completa en paralelo y no es un prerrequisito para este desarrollo.
