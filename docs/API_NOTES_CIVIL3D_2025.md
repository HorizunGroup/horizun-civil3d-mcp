# Notas de API de Civil 3D 2025: hechos verificados, limitaciones y trampas

> Origen: hallazgos del conector anterior (retirado el 2026-10-04), conservados porque siguen vigentes. Las herramientas `herramientas/apidump` y `scripts/csharp/*` que se citan quedaron archivadas fuera del repo; el equivalente actual es `tools/Horizun.Civil3D.ApiProbe` (ver `docs/api-probes/`).

Entorno: Civil 3D 2025 (`25.0s (LMS Tech)`), `AeccDbMgd.dll` v13.7.145.0, .NET 8. Todas las firmas de abajo se verificaron con la herramienta `herramientas/apidump` (MetadataLoadContext sobre las DLL de `C_References`) o ejecutando código en vivo.

## 1. Cómo inspeccionar la API sin abrir Civil 3D

`herramientas/apidump/` (proyecto consola net8 con `System.Reflection.MetadataLoadContext 8.0.0`):
```bash
dotnet run -- Autodesk.Civil.DatabaseServices.SurfaceAnalysis Autodesk.Civil.DatabaseServices.Surface
ASM=AcDbMgd.dll dotnet run -- Autodesk.AutoCAD.DatabaseServices.DwgFiler      # otra DLL
dotnet run -- LISTTYPES Grading                                                  # buscar tipos por nombre
```
Imprime métodos (M, con `abstract`), propiedades (P, get/set), constructores (C) y valores de enum (E).

## 2. Superficies

| Tema | API verificada |
|---|---|
| Análisis de elevaciones | `Surface.Analysis` → `SurfaceAnalysis.GetElevationData()` / `SetElevationData(SurfaceAnalysisElevationData[])`; `SurfaceAnalysisElevationData(double min, double max, Color scheme)` con props `MinimumElevation`, `MaximumElevation`, `Scheme` (get/set). Igual para Slope, SlopeArrow, Direction, Contour, UserDefinedContour, Watershed |
| No existe | `CalculateElevationRegions` ni equivalente → los rangos "automáticos" se calculan en el plugin (min–max del `GetGeneralProperties()`) |
| Estilo | `Surface.StyleId` (set), `Entity.StyleName`; `SurfaceStyle.GetDisplayStylePlan/Model(SurfaceDisplayStyleType)` → `DisplayStyle.Visible/Color/Layer/Linetype/Lineweight` |
| SurfaceDisplayStyleType | `Points, Triangles, Boundary, MajorContour, MinorContour, UserContours, Gridded, Directions, Elevations, Slopes, SlopeArrows, Watersheds` |
| SurfaceElevationStyle | `NumberOfRanges`, `GroupValuesBy` (`EqualInterval/Quantile/StandardDeviation`), `ColorScheme/CutScheme/FillScheme` (`Blues, Greens, Hydro, Land, Pastels, Rainbow, Reds`), `DisplayEntityMode`, `RangePrecision`, `LegendStyleId` |
| Duplicar estilo | `StyleBase.CopyAsSibling(string)` ; colección `civilDoc.Styles.SurfaceStyles` (`Add(name)`, `Remove`) |
| Nombre | `Autodesk.Civil.DatabaseServices.Entity.Name` (get/set) → renombrar superficies, feature lines, sites |
| Propiedades | `GetGeneralProperties()` → `MinimumElevation, MaximumElevation, MeanElevation` (**media de vértices, NO ponderada por área**), `NumberOfPoints`. `TinSurface.GetTinProperties()` → triángulos; `GetTerrainProperties()` → `SurfaceArea2D/3D`, pendientes |
| TinVolumeSurface | `Create(name, baseId, compId[, styleId])`, `GetVolumeProperties()` (**requiere transacción de escritura**) → `Unadjusted/Adjusted Cut/Fill/NetVolume`, `BaseSurface`, `ComparisonSurface` (**solo lectura**: para cambiar la comparación hay que borrar y recrear), `CutFactor/FillFactor` |
| Reconstrucción | `Surface.AutoRebuild` (las isopacas venían en false → no se actualizan solas), `Rebuild()`, `IsOutOfDate` |
| Muestreo | `FindElevationAtXY` (excepción fuera de la superficie), `SampleElevations`, `GetBoundedVolumes(Point3dCollection)` |
| TIN | `TinSurface.Create(name, styleId)`, `PasteSurface(ObjectId)`, `BreaklinesDefinition.AddStandardBreaklines(ObjectIdCollection, midOrdinate, maxDist, weedDist, weedAngle)` (acepta Polyline3d **y FeatureLine** → breakline dinámica), `BoundariesDefinition.AddBoundaries(ObjectIdCollection, midOrd, SurfaceBoundaryType.Outer|Hide|Show|DataClip, nonDestructive)`, `ExtractBorder(SurfaceExtractionSettingsType.Model)` |
| Leer definición | `TinSurface.Operations[i]` (no enumerable con foreach; usar índice), `BreaklinesDefinition[i]` → grupo con `Description`, `BreaklineType`, `Count`, items con propiedad `Vertices` |
| Borrar | `surface.Erase()` (abrir ForWrite) |

## 3. Feature lines y sites

| Tema | API |
|---|---|
| Crear FL | `FeatureLine.Create(string name, ObjectId entityId, ObjectId siteId)` desde Polyline/Polyline3d (la entidad original se conserva) |
| Puntos | `fl.GetPoints(FeatureLinePointType.AllPoints)` → Point3dCollection |
| Sites | `Site.Create(civilDoc, name)`, `civilDoc.GetSiteIds()`, `site.GetFeatureLineIds()`, `site.Name` (renombrable) |
| Feature lines que pertenecen a gradings | NO aparecen en `site.GetFeatureLineIds()`; sí en el espacio modelo (clase `AeccDbFeatureLine`) |

## 4. Gradings — la gran limitación

- `Autodesk.Civil.DatabaseServices.Grading` existe **vacía** (sin miembros). No hay `GradingGroup` administrado (solo `AeccDbGradingGroup` nativo). El upstream intenta `site.GradingGroups.Add` por reflexión → no existe.
- El COM (`entity.AcadObject`) del grading expone una interfaz genérica tipo parcela (`Statistics` → `Area`, `Perimeter`; `Parent` = site). El COM del site **no** tiene `GradingGroups`.
- `DwgOut` con un `DwgFiler` propio (implementado en `scripts/csharp/grad_dump.cs`) **no** contiene criterio, talud ni objetivo: se guardan en la topología interna del site.
- Criterios sí se leen: `civilDoc.Styles.GradingCriteriaSets` → `GradingCriteriaSet.GradingCriteriaIds()` → `GradingCriteria` (`Target`, `SurfaceProjectionType`, `CutSlope/FillSlope/Slope/Distance/RelativeElevation` como `PropertyDouble.Value`). Un dibujo real de cliente tenía sets inflados: "Standard Set" (8), "Conjunto estándar" (507), "Basic Set" (675).
- Criterio actual de la barra: `civilDoc.Settings.GetSettings<SettingsCmdGradingTools>().GradingLayoutTools` → `GradingCriteriaSetId.Value`, `CriteriaId.Value` (**escribibles**). No hay setting de "grupo actual" ni de "site actual".
- **Comandos nativos** (extraídos del CUIX `C:\Program Files\Autodesk\AutoCAD 2025\C3D\UserDataCache\Support\c3d.cuix`): `_AeccGradingTools`, `_AeccCreateGradingGroup`, `_AeccSelectGradingGroup`, `CreateGrading` (prompts de línea de comandos), `_AeccCreateGradingInfill`, `_AeccEditGrading`, `_AeccDeleteGradings`, `_AeccChangeGradingGroup`, `_AeccGradingGroupProps`, `_AeccGradingProps`, `_AeccGradingEditor`, `_AeccGradingElevEditor`, `_AeccGradingVolumeTools`, `_AeccCreateDetachedGradingSurf`, `_AeccFeatureLinesFromCorridor`, `_AeccEditGradingStyle`.
- Prompts de `CreateGrading`: `Select the feature:` (**prompt de punto**: no acepta ObjectId; sí un punto WCS `*x,y,z` sobre la FL, preferible con la FL aislada con `ISOLATEOBJECTS`) → si el grupo actual es de otro site: *"This feature is not in the Grading Group's site."*
- Diálogos: `_AeccCreateGradingGroup` → "Create Grading Group" (Name `Edit#17144`, Description `Edit#17143`, casillas "Automatic surface creation" y "Use the Group Name" `Button#17087`, estilo de superficie, teselación, "Volume base surface") → "Create Surface" (OK). Sin grupo actual, `CreateGrading` abre "Select Grading Group" (Site name / Group name / botón "Create a Grading Group" `Button#17050`).
- **Bloqueo definitivo**: el selector de site de "Select Grading Group" es un control propio (`#32770` que contiene `ComboBox#1237` con **count=0** aunque se despliegue); su botón "P" pide `Select object in site:` y rechaza tanto ObjectId como puntos escritos; preseleccionar (pickfirst) la FL no cambia el site. Resultado: **no se logró crear un grading group en un site distinto al actual sin intervención manual**.
- Comunidad: sin API ([foro Autodesk](https://forums.autodesk.com/t5/civil-3d-customization-forum/how-to-create-an-grading-with-the-net-api/td-p/12211851)); Dynamo sin nodos de grading ([foro Dynamo](https://forum.dynamobim.com/t/create-grading-from-feature-line/62971)); truco conocido: tener un grading de plantilla y mover/editar su feature line.
- **Decisión**: motor geométrico (polilíneas 3D + breaklines) → ver `docs/ROADMAP.md` y `horizun_c3d_grading`.

## 5. Comandos, prompts y UCS

- `Editor.CommandAsync(object[])` solo dentro de `ExecuteInCommandContextAsync`. Acepta string, double, Point3d, ObjectId.
- Eventos para registrar prompts: `Editor.PromptingForPoint/Entity/Keyword/Selection/String/Double/Integer/Distance/Angle` (args `.Options.Message`).
- **UCS**: un dibujo real de cliente tenía UCS no universal (vista girada, "Center X 140.367" en propiedades de vista vs WCS 918 727). Todo punto por línea de comandos debe ir como `"*X,Y,Z"` (WCS). `ZOOM _W *x1,y1 *x2,y2` funciona así.
- `CMDACTIVE` = 1 comando, +32 diálogo (33 = comando con diálogo modal abierto).
- Historial de la línea de comandos: `Autodesk.AutoCAD.Internal.Utils.GetLastCommandLines(n, true)`.
- `ISOLATEOBJECTS` con `{handle}` + `""` funciona; `UNISOLATEOBJECTS` para revertir.
- `Entity.Visible = false` oculta objetos no seleccionables (se usó para evitar seleccionar la polilínea coincidente con la FL; **siempre restaurar**).
- Un modal abierto en Civil bloquea todas las peticiones del plugin que requieren contexto de comando.

## 6. Otros

- `Autodesk.AutoCAD.Colors.Color`: `FromRgb(r,g,b)`, `FromColorIndex(ColorMethod.ByAci, n)`; `ColorMethod`, `ColorIndex`, `Red/Green/Blue`.
- Texto dentro de una polilínea (para deducir el nombre de una zona): recorrer `AcDbText`/`AcDbMText` del espacio modelo y filtrar por el recuadro de la polilínea (`scripts/csharp/inspect_pl.cs`). Limitación: usa recuadro, no el polígono exacto.
