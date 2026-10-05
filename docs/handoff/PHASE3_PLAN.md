# PHASE 3 PLAN: vías, secciones, anotación y AutoCAD (basado en el estudio del mercado)

> Fecha: 2026-10-01. Fuentes:
> - auditoría del código de Sacred-G/Civil3D-mcp en el conector anterior (retirado, archivado fuera del repo);
> - estudio web de 13 MCP de AutoCAD;
> - sondeos de la API en `docs/api-probes/2025/AeccDbMgd.phase3-roads.txt`, `AeccDbMgd.phase3-labels.txt` y `acdbmgd.phase3-annotation.txt`.

## 1. Qué hay en el mercado

### Civil 3D
- **Sacred-G** es el más amplio, con 216 herramientas, pero tiene dos fallas de fondo:
  1. Su reflexión se traga los errores y responde `success:true` aunque el método no exista.
  2. Tiene 34 herramientas que no están conectadas al despachador.
- Escrituras **falsas** confirmadas en el código:
  - alineamiento: add_tangent, add_curve, add_spiral y station equation;
  - perfil: delete_pvi y add_curve;
  - recubrimiento de red de presión;
  - filtros de grupos de puntos;
  - peralte;
  - sheet sets y láminas planta-perfil.
- Escrituras que **siempre fallan**:
  - crear vista de perfil;
  - agregar tubería (gravedad y presión);
  - crear ensamblaje y subensamblaje.
- **No existen** en Sacred-G: crear corredor, crear vistas de sección, LandXML, view frames y cotas.
- **Reales en Sacred-G:** alineamiento desde polilínea, perfil desde superficie, perfil por layout vacío, grupos y líneas de muestreo, rebuild de corredor, puntos COGO, viewports y PDF.
- **Otros:** HMK CAD Pilot es comercial y cerrado (87 acciones AutoCAD y 53 Civil 3D). Autodesk Assistant de AutoCAD 2027 es **solo lectura**. No hay MCP oficial de Autodesk que escriba dibujos.

### AutoCAD
- **U-C4N/Autocad-MCP** es el más completo en anotación: todos los tipos de cota, estilos de cota, MLeader, tablas, layouts, viewports, page setups y batch plot. Funciona por COM, que es lento y queda fuera del proceso de AutoCAD.
- **puran-water** es el más popular, por AutoLISP.
- Ninguno combina las garantías de Horizun en cada escritura: dry run, token y re-lectura.

## 2. Qué hará Horizun (y cómo es mejor)

Usa API .NET tipada dentro del proceso, nunca reflexión adivinada. Cada escritura pasa por dry run, token, commit en contexto de comando (un UNDO) y re-lectura. Todas las firmas siguientes están confirmadas en la DLL de 2025.

### Bloque A — Vías (siguiente bloque largo)

| Herramienta / acción | API confirmada | Verificación |
|---|---|---|
| `horizun_c3d_alignment` create_from_polyline | `Alignment.Create(civilDoc, PolylineOptions{PlineId, AddCurvesBetweenTangents, EraseExistingEntities}, name, siteId, layerId, styleId, labelSetId)` | entidades, longitud y estaciones re-leídas |
| alignment create_from_pis (tangentes y curvas por radio) | `Entities.AddFixedLine(Point3d,Point3d)` y `AddFreeCurve(prev, next, radius, CurveParamType...)` | tipo de cada entidad, radio, longitud |
| alignment offset | `Alignment.CreateOffsetAlignment(name, parentId, offset, styleId[, start, end])` | desfase medido en estaciones de muestra |
| alignment station_offset (lectura) | `StationOffset` / `PointLocation` | ida y vuelta XY ↔ estación |
| `horizun_c3d_profile` from_surface | `Profile.CreateFromSurface(name, alignmentId, surfaceId, layerId, styleId, labelSetId[, offset, start, end])` | cota del perfil = superficie en N estaciones |
| profile layout (PVI + curvas) | `Profile.CreateByLayout` + `PVIs.AddPVI / AddPVISymParabola / AddPVIArc / AddPVIAsymParabola` | PVI re-leídos; K y longitud de curva |
| profile elevation_at_station, check_k (criterios como parámetro) | `ElevationAt` | lectura |
| profile_view create | `ProfileView.Create(alignmentId, insertPoint, name, bandSetId, styleId)` | existe y cubre el rango de estaciones |
| `horizun_c3d_sections` sample_lines | `SampleLineGroup.Create(name, alignmentId)` + `SampleLine.Create(name, groupId, station)` | estaciones re-leídas |
| section_views | `SectionView.Create(name, sampleLineId, location)` | una vista por línea |
| `horizun_c3d_corridor` create | `CorridorCollection.Add(name, baseline, alignmentId, profileId, region, assemblyId)` + `Rebuild()` | líneas base, regiones, ensamblaje y estado del rebuild |
| corridor add_region / stations | `BaselineRegions.Add(name, assemblyId, start, end)`, `AddStation` | regiones re-leídas |
| corridor surface | `CorridorSurfaces.Add(name, styleId)` + códigos de enlace (sondear `CorridorSurface`) | superficie creada, puntos > 0 |
| corridor volumes vs EG | volumen transitorio (ya existe en surface) | corte y relleno |

Al dibujo de ensayo se le agregan una alineación recta con una curva de radio conocido, un perfil con PVI conocidos (pendientes y K exactos) y un ensamblaje mínimo. Antes hay que sondear cómo crear ensamblajes y subensamblajes en 2025: `SubassemblyCollection` solo trae `Add(desde entidad)`, así que falta confirmar la importación de stock (`ImportStockSubassembly` no aparece en la DLL de 2025). Si no existe, se usa `AssemblyCollection.ImportAssembly(atc, itemId, ...)`, que sí existe, desde catálogo. Lo que no tenga API se rechaza por nombre.

### Bloque B — Anotación Civil

| Acción | API confirmada |
|---|---|
| etiquetas de estación del alineamiento (mayor/menor) | `AlignmentStationLabelGroup.Create / CreateMajor(styleId, alignmentId, increment)` |
| etiquetas de curvas/tangentes | `AlignmentCurveLabel.Create(arc, styleId)`, `AlignmentTangentLabel.Create(line, styleId)` |
| estación-desfase | `StationOffsetLabel.Create(alignmentId, styleId, markerId, Point2d)` |
| PVI del perfil | `ProfilePVILabelGroup.Create(profileViewId, profileId, styleId)` |
| estación-cota en vista de perfil | `StationElevationLabel.Create(...)` |
| cota de superficie, pendiente y curvas de nivel | `SurfaceElevationLabel.Create`, `SurfaceSlopeLabel.Create`, `SurfaceContourLabelGroup.Create(surfaceId, Point2dCollection, ...)` |
| nota y segmento general | `NoteLabel.Create`, `GeneralSegmentLabel.Create(featureId, ratio, ...)` |
| estilos de etiqueta: listar y duplicar | `LabelStyleCollection` + `CopyAsSibling` |

### Bloque C — AutoCAD (dentro del mismo MCP)

Se toman las 20 operaciones de mayor valor según el estudio:

1. **Capas:** gestión completa (crear, color, tipo de línea cargando desde acad.lin, grosor, congelar, bloquear) y estados de capa con el LayerStateManager nativo.
2. **Selección:** consulta por tipo, capa, bloque o ventana, con área y longitud reales de la curva. Propiedades en lote.
3. **Transformaciones:** mover, copiar, rotar, escalar, reflejar, desfasar, descomponer y unir.
4. **Cotas:** lineales y alineadas (constructores confirmados), en cadena (base y continua), angulares, radiales, diámetro y ordenadas. Estilos de cota con creación, modificación, actual y limpieza de overrides. Hay que llamar `RecomputeDimensionBlock` antes de re-leer.
5. **Textos y anotación:** estilos de texto, escalas anotativas, MText, MLeader (secuencia AddLeader → AddLeaderLine → vértices, y asignar un MText nuevo).
6. **Bloques y tablas:** definir e insertar con atributos (crear las AttributeReferences), atributos en lote para rótulos, propiedades de bloques dinámicos y tablas desde filas.
7. **Layouts y PDF:** crear layouts desde plantilla, page setup, viewports (`On=true` después de agregarlo, congelar capas por viewport, escala, bloqueo) y PDF. Para el PDF hay que trabajar en contexto de aplicación con `BACKGROUNDPLOT=0`, y se verifica que el archivo exista y tenga páginas.
8. **Mantenimiento:** purge, audit, xrefs y chequeo de estándares contra un JSON (capas, estilos de cota y de texto).

### Bloque D — Redes y datos

- Red por gravedad: `AddLinePipe` y `AddStructure` con familia y tamaño del catálogo, con validación de recubrimiento y pendientes.
- Red de presión.
- Grupos de puntos con `StandardPointGroupQuery`.
- Data shortcuts y LandXML, solo lo que la API permita. Lo demás se rechaza por nombre.
- Tablas Civil, view frames y láminas planta-perfil: sin API pública conocida, así que se rechazan por nombre o se ofrece la alternativa AutoCAD (layouts y viewports).

## 3. Orden recomendado

1. Sesión de pruebas en vivo de la v0.5.0 (pendiente).
2. **Bloque A (vías)** con su ampliación del dibujo de ensayo.
3. Bloque B junto con el C.
4. Bloque D.

Cada bloque sigue el mismo ciclo: sondear, implementar, probar sin Civil 3D, hacer una sola sesión en vivo larga y corregir todo de una vez.

## 4. Límites y oportunidades confirmados por el estudio web (2026-10-01)

**Otros MCP:**
- hjlrosales/Civil3D_MCP es casi solo lectura. Solo escribe tuberías y renombra.
- xuantinhnbs-rgb (COM, Civil 3D 2026) dice crear alineamientos, perfiles, vistas de perfil, corredores y secciones por COM, pero no está verificado línea por línea.
- Varios MCP son solo un "ejecutor de C#" (Roslyn) sin herramientas tipadas.
- HMK CAD Pilot es cerrado.
- No hay MCP oficial de Autodesk para AutoCAD ni Civil 3D. El Assistant de 2027 solo cambia estilos.

**Sin API pública: se rechaza por nombre y se ofrece una alternativa.**

| Capacidad | Alternativa |
|---|---|
| Crear gradings | Motor geométrico (ya hecho) |
| View frames y láminas planta-perfil | Layouts y viewports de AutoCAD (Bloque C) |
| Crear intersecciones | — |
| Cálculo del asistente de peralte | Solo `AddUserDefinedCurve` y edición manual de estaciones críticas |
| Exportar LandXML desde .NET | **Escribir el XML nosotros** desde los datos leídos (superficies TIN, alineamientos, perfiles) y verificarlo releyendo el archivo; es una oportunidad de diferenciarse |

**Sí existen:**
- `SectionViewGroupCollection.Add` (grupos de vistas de sección).
- `CorridorSurfaces.Add` con códigos de enlace y de feature line, más bordes.
- Listas de materiales QTO: `SampleLineGroup.MaterialLists`, importar criterios, `ReportQuantities`.
- Corredores y líneas base desde feature line.
- Data shortcuts parciales: `CreateReference`, estado y reparación. Promover y publicar probablemente no existe, se confirma con sondeo.

**Referencia útil:** Dynamo para Civil 3D 2025.1 usa `Assembly.Import` (equivalente a `AssemblyCollection.ImportAssembly`). Es el camino para traer ensamblajes de catálogo cuando no hay una API de subensamblajes de stock.
