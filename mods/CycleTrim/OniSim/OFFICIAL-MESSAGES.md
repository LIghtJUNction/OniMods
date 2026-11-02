# Official SimDLL managed interface catalogue

This inventory records identifiers, signatures, field layouts and call contracts from static inspection of the installed official Linux x64 binaries. It contains no official implementation source. It does not establish implementation completeness or runtime compatibility.

Evidence date: 2026-10-20. Tool: `ilspycmd 10.0.1.8346 --disable-updatecheck -t TYPE`. Decompiled implementation output remains only in `/tmp/re/oni-simdll/catalogue-*`. No game, official library, simulator or tests were executed. No community Rust simulator source, binary or network source was consulted.

| Binary | SHA-256 |
| --- | --- |
| Assembly-CSharp.dll | `6db652990ad1038e86b3493ed43004e12bfd61e62a4be60c787a9a9b9ac52ee1` |
| libSimDLL.so | `85111cd0b1fc14c79c785581d6c055280b9d48ba705776f3d14eb0a7b239c12d` |

## Coverage boundary

`SimMessageHashes` declares **62 distinct messages**. `SimMessages` declares **59 methods** (58 public and 1 private). Overloads and convenience wrappers explain the different counts. Six recognized messages do not establish this interface or the simulator.

A full replacement needs all retained messages to have an explicit supported, deliberately unsupported or proven obsolete status; every exported ABI call; stable grid/output storage; native command scheduling; component handle lifetimes; all output spans; radiation; and save/load compatibility. Recognizing an ID or returning an empty descriptor does not implement its behavior.

## Initialization dependencies

The inspected SaveLoader has two concrete call chains:

1. Existing save: initialize diagnostic callback; create element table; allocate cells; create disease table; clear unoccupied cells; load native world blob; start; load managed state and visibility; define world offsets.
2. New world generation result: initialize callback; create element table; allocate cluster cells; define world offsets; create disease table; clear unoccupied cells; load each generated world native blob; start.

Normal startup therefore depends on native Load, including multiple-world blob assembly. SimDataInitializeFromCells alone does not replace that path. Start returns a GameDataUpdate pointer and binds native grid, backwall, flow and texture pointers to managed consumers.

| Message | Meaningful serialized content | Length passed by managed caller |
| --- | --- | --- |
| Elements_CreateTable | i32 count; count * 164 B element records; count UTF-8 names, each i32 byte length + bytes | GetBuffer().Length, including capacity padding |
| Disease_CreateTable | i32 disease count, i32 element count; each disease name, 76 B header, elementCount * 29 B growth records | actual stream Length, using GetBuffer() storage |
| DefineWorldOffsets | i32 count; repeated i32 offsetX, offsetY, sizeX, sizeY | actual stream Length |
| SimData_InitializeFromCells | 14 B header: i32 width,height; u32 seed; bool8 radiation,headless; then N*16 B Cell, N*12 B DiseaseCell, N*12 B SimBackwall | buffer capacity; meaningful length 14 + 40*N |
| SimData_ResizeAndInitializeVacuumCells | six i32: gridWidth,gridHeight,width,height,offsetX,offsetY | buffer capacity; meaningful length 24 B |
| SimData_FreeCells | four i32: width,height,offsetX,offsetY | buffer capacity; meaningful length 16 B |

These binary writers use little endian scalars. BinaryWriter bool is one byte. UTF-8 strings use an i32 byte count, not a variable-length string prefix. The inspected disease writer uses GetBuffer plus actual Length; describing it as ToArray would be inaccurate for this binary.

SetWorldZones receives byte[N], one zone byte per cell. SubworldZoneRenderData.GenerateTexture maps zone enum value 7 to byte 255; other values are cast to a byte.

## Frame submission and results

1. Game.UnsafeSim200ms gathers regions for discovered worlds, with sunlight and cosmic radiation intensities.
2. SimMessages.NewGameFrame sends `-775326397` using the single-message export and total byte length `28 * regionCount`. Regions are consecutive records, not calls to SIM_HandleMessages.
3. Immediately afterward, Game.StepTheSim sends `PrepareGameData=1078620451` with the full raw Grid.Visible array and byte length N. It receives a GameDataUpdate pointer, updates grid pointers and consumes result spans.
4. Cell changes, falling liquid, dig/spawn, callbacks and thermal events are consumed from that descriptor. Managed conduit and energy updates can enqueue further commands.
5. When numFramesProcessed is positive, World.UpdateCellInfo applies solid/navigation/render changes and releases accumulated basic callback handles; lists are then cleared.

The submitted dt can be 0.2 or 0. SimEveryTick uses twelve 1/60-second subticks per 0.2-second simulation interval and can take a zero-dt path. Managed code establishes call order, but does not establish native queue drain, worker synchronization, result lifetime or completion semantics. Do not independently advance physics again on PrepareGameData without native evidence.

| NewGameFrame field | Byte offset | Type |
| --- | ---: | --- |
| elapsedSeconds | 0 | f32 |
| minX / minY | 4 / 8 | i32 |
| maxX / maxY | 12 / 16 | i32 |
| currentSunlightIntensity | 20 | f32 |
| currentCosmicRadiationIntensity | 24 | f32 |

The wrapper clamps minX to 0..width-1, maxX to 0..width, and both Y limits to 0..height-1. Native coordinate inclusivity remains a separate question.

## Complete message identifiers

Sizes below are computed statically from the declared unmanaged sizeof layouts with Pack=4; the official code was not loaded to measure them. bool8 is an unmanaged one-byte C# bool in a pointer payload, not Marshal.SizeOf(bool). Pointers describe Linux x64. Unknown shapes remain explicit.

| Category | Message | Signed ID | Payload | Sender |
| --- | --- | ---: | --- | --- |
| initialization/world | Elements_CreateTable | `1108437482` | serialized bytes; see initialization table | CreateSimElementsTable |
| initialization/world | Elements_CreateInteractions | `-930289787` | CreateElementInteractionsMsg (12 B) | CreateElementInteractions |
| initialization/world | SetWorldZones | `-457308393` | raw byte[N], one zone per grid cell | SubworldZoneRenderData.InitSimZones |
| initialization/world | ModifyCellWorldZone | `-449718014` | CellWorldZoneModification (8 B) | ModifyCellWorldZone |
| initialization/world | Disease_CreateTable | `825301935` | serialized bytes; see initialization table | CreateDiseaseTable |
| save/load | Load | `-672538170` | opaque native save bytes (length supplied separately) | Sim.Load / Sim.LoadWorld |
| initialization/world | Start | `-931446686` | empty; returns GameDataUpdate* | Sim.Start |
| initialization/world | AllocateCells | `1092408308` | width:i32, height:i32, radiation:bool8, headless:bool8 (10 B) | Sim.AllocateCells |
| initialization/world | ClearUnoccupiedCells | `-1836204275` | empty | SaveLoader |
| initialization/world | DefineWorldOffsets | `-895846551` | serialized bytes; see initialization table | DefineWorldOffsets |
| frame/control | PrepareGameData | `1078620451` | Grid.Visible raw bytes, length N; returns GameDataUpdate* | Game.StepTheSim |
| initialization/world | SimData_InitializeFromCells | `2062421945` | serialized bytes; see initialization table | SimDataInitializeFromCells |
| initialization/world | SimData_ResizeAndInitializeVacuumCells | `-752676153` | serialized bytes; see initialization table | SimDataResizeGridAndInitializeVacuumCells |
| initialization/world | SimData_FreeCells | `-1167792921` | serialized bytes; see initialization table | SimDataFreeCells |
| frame/control | SimFrameManager_NewGameFrame | `-775326397` | NewGameFrame[regions] (28 B each) | NewGameFrame |
| cell command | Dig | `833038498` | DigMessage (12 B) | Dig |
| cell command | ModifyCell | `-1252920804` | ModifyCellMessage (28 B) | ModifyCell |
| cell command | ModifyCellEnergy | `818320644` | ModifyCellEnergyMessage (16 B) | ModifyEnergy |
| cell command | SetInsulationValue | `-898773121` | SetCellFloatValueMessage (8 B) | SetInsulation |
| cell command | SetStrengthValue | `1593243982` | SetCellFloatValueMessage (8 B) | SetStrength |
| frame/control | SetVisibleCells | `-563057023` | unknown; no direct managed integer-constant sender found | retained enum only in inspected sender search |
| cell command | ChangeCellProperties | `-469311643` | CellPropertiesMessage (12 B) | SetCellProperties, ClearCellProperties |
| component | AddBuildingHeatExchange | `1739021608` | AddBuildingHeatExchangeMessage (44 B) | AddBuildingHeatExchange |
| component | ModifyBuildingHeatExchange | `1818001569` | ModifyBuildingHeatExchangeMessage (44 B) | ModifyBuildingHeatExchange |
| component | ModifyBuildingEnergy | `-1348791658` | ModifyBuildingEnergyMessage (16 B) | ModifyBuildingEnergy |
| component | RemoveBuildingHeatExchange | `-456116629` | RemoveBuildingHeatExchangeMessage (8 B) | RemoveBuildingHeatExchange |
| component | AddBuildingToBuildingHeatExchange | `-1338718217` | RegisterBuildingToBuildingHeatExchangeMessage (8 B) | RegisterBuildingToBuildingHeatExchange |
| component | AddInContactBuildingToBuildingToBuildingHeatExchange | `-1586724321` | AddBuildingToBuildingHeatExchangeMessage (12 B) | AddBuildingToBuildingHeatExchange |
| component | RemoveBuildingInContactFromBuildingToBuildingHeatExchange | `-1993857213` | RemoveBuildingInContactFromBuildingToBuildingHeatExchangeMessage (8 B) | RemoveBuildingInContactFromBuildingToBuildingHeatExchange |
| component | RemoveBuildingToBuildingHeatExchange | `697100730` | RemoveBuildingToBuildingHeatExchangeMessage (8 B) | RemoveBuildingToBuildingHeatExchange |
| frame/control | SetDebugProperties | `-1683118492` | DebugProperties (12 B) | SetDebugProperties |
| cell command | MassConsumption | `1727657959` | MassConsumptionMessage (16 B) | ConsumeMass (two overloads) |
| cell command | MassEmission | `797274363` | MassEmissionMessage (24 B) | EmitMass |
| component | AddElementConsumer | `2024405073` | AddElementConsumerMessage (12 B) | AddElementConsumer |
| component | RemoveElementConsumer | `894417742` | RemoveElementConsumerMessage (8 B) | RemoveElementConsumer |
| component | SetElementConsumerData | `1575539738` | SetElementConsumerDataMessage (12 B) | SetElementConsumerData |
| component | AddElementEmitter | `-505471181` | AddElementEmitterMessage (16 B) | AddElementEmitter |
| component | ModifyElementEmitter | `403589164` | ModifyElementEmitterMessage (32 B) | ModifyElementEmitter |
| component | RemoveElementEmitter | `-1524118282` | RemoveElementEmitterMessage (8 B) | RemoveElementEmitter |
| component | AddElementChunk | `1445724082` | AddElementChunkMessage (32 B) | AddElementChunk |
| component | RemoveElementChunk | `-912908555` | RemoveElementChunkMessage (8 B) | RemoveElementChunk |
| component | SetElementChunkData | `-435115907` | SetElementChunkDataMessage (12 B) | SetElementChunkData |
| component | MoveElementChunk | `-374911358` | MoveElementChunkMessage (8 B) | MoveElementChunk |
| component | ModifyElementChunkEnergy | `1020555667` | ModifyElementChunkEnergyMessage (8 B) | ModifyElementChunkEnergy |
| component | ModifyChunkTemperatureAdjuster | `-1387601379` | ModifyElementChunkAdjusterMessage (16 B) | ModifyElementChunkTemperatureAdjuster |
| cell command | ModifyBackwallData | `-605421515` | SetBackwallDataMsg (16 B) | SetBackwallData |
| component | AddDiseaseEmitter | `1486783027` | AddDiseaseEmitterMessage (4 B) | AddDiseaseEmitter |
| component | ModifyDiseaseEmitter | `-1899123924` | ModifyDiseaseEmitterMessage (20 B) | ModifyDiseaseEmitter |
| component | RemoveDiseaseEmitter | `468135926` | RemoveDiseaseEmitterMessage (8 B) | RemoveDiseaseEmitter |
| component | AddDiseaseConsumer | `348345681` | unknown; no direct managed integer-constant sender found | retained enum only in inspected sender search |
| component | ModifyDiseaseConsumer | `-1822987624` | unknown; no direct managed integer-constant sender found | retained enum only in inspected sender search |
| component | RemoveDiseaseConsumer | `-781641650` | unknown; no direct managed integer-constant sender found | retained enum only in inspected sender search |
| cell command | ConsumeDisease | `-1019841536` | ConsumeDiseaseMessage (16 B) | ConsumeDisease |
| cell command | CellDiseaseModification | `-1853671274` | CellDiseaseModification (12 B) | ModifyDiseaseOnCell |
| frame/control | ToggleProfiler | `-409964931` | empty | DebugHandler.OnKeyDown |
| frame/control | SetSavedOptions | `1154135737` | SetSavedOptionsMessage (2 B) | SetSavedOptionValue |
| radiation | CellRadiationModification | `-1914877797` | CellRadiationModification (12 B) | ModifyRadiationOnCell |
| radiation | RadiationSickness | `-727746602` | unknown; no direct managed integer-constant sender found | retained enum only in inspected sender search |
| radiation | AddRadiationEmitter | `-1505895314` | AddRadiationEmitterMessage (36 B) | AddRadiationEmitter |
| radiation | ModifyRadiationEmitter | `-503965465` | ModifyRadiationEmitterMessage (40 B) | ModifyRadiationEmitter |
| radiation | RemoveRadiationEmitter | `-704259919` | RemoveRadiationEmitterMessage (8 B) | RemoveRadiationEmitter |
| radiation | RadiationParamsModification | `377112707` | RadiationParamsModification (8 B) | ModifyRadiationParams |

## Input record layouts

Offsets are bytes. Explicit and implicit padding is not gameplay state. Some official stack payloads leave padding uninitialized. ModifyBuildingHeatExchangeMessage.callbackIdx carries the existing sim handle in the modify wrapper. The building-to-building register operation uses enum AddBuildingToBuildingHeatExchange; add-contact uses AddInContactBuildingToBuildingToBuildingHeatExchange.

| Record | Size | Fields in declaration order |
| --- | ---: | --- |
| AddElementConsumerMessage | 12 | cellIdx: i32 @0; callbackIdx: i32 @4; radius: u8 @8; configuration: u8 @9; elementIdx: u16 @10 |
| SetElementConsumerDataMessage | 12 | handle: i32 @0; cell: i32 @4; consumptionRate: f32 @8 |
| RemoveElementConsumerMessage | 8 | handle: i32 @0; callbackIdx: i32 @4 |
| AddElementEmitterMessage | 16 | maxPressure: f32 @0; callbackIdx: i32 @4; onBlockedCB: i32 @8; onUnblockedCB: i32 @12 |
| ModifyElementEmitterMessage | 32 | handle: i32 @0; cellIdx: i32 @4; emitInterval: f32 @8; emitMass: f32 @12; emitTemperature: f32 @16; maxPressure: f32 @20; diseaseCount: i32 @24; elementIdx: u16 @28; maxDepth: u8 @30; diseaseIdx: u8 @31 |
| RemoveElementEmitterMessage | 8 | handle: i32 @0; callbackIdx: i32 @4 |
| AddRadiationEmitterMessage | 36 | callbackIdx: i32 @0; cell: i32 @4; emitRadiusX: i16 @8; emitRadiusY: i16 @10; emitRads: f32 @12; emitRate: f32 @16; emitSpeed: f32 @20; emitDirection: f32 @24; emitAngle: f32 @28; emitType: i32 @32 |
| ModifyRadiationEmitterMessage | 40 | handle: i32 @0; cell: i32 @4; callbackIdx: i32 @8; emitRadiusX: i16 @12; emitRadiusY: i16 @14; emitRads: f32 @16; emitRate: f32 @20; emitSpeed: f32 @24; emitDirection: f32 @28; emitAngle: f32 @32; emitType: i32 @36 |
| RemoveRadiationEmitterMessage | 8 | handle: i32 @0; callbackIdx: i32 @4 |
| AddElementChunkMessage | 32 | gameCell: i32 @0; callbackIdx: i32 @4; mass: f32 @8; temperature: f32 @12; surfaceArea: f32 @16; thickness: f32 @20; groundTransferScale: f32 @24; elementIdx: u16 @28; pad0: u8 @30; pad1: u8 @31 |
| RemoveElementChunkMessage | 8 | handle: i32 @0; callbackIdx: i32 @4 |
| SetElementChunkDataMessage | 12 | handle: i32 @0; temperature: f32 @4; heatCapacity: f32 @8 |
| MoveElementChunkMessage | 8 | handle: i32 @0; gameCell: i32 @4 |
| ModifyElementChunkEnergyMessage | 8 | handle: i32 @0; deltaKJ: f32 @4 |
| ModifyElementChunkAdjusterMessage | 16 | handle: i32 @0; temperature: f32 @4; heatCapacity: f32 @8; thermalConductivity: f32 @12 |
| AddBuildingHeatExchangeMessage | 44 | callbackIdx: i32 @0; elemIdx: u16 @4; pad0: u8 @6; pad1: u8 @7; mass: f32 @8; temperature: f32 @12; thermalConductivity: f32 @16; overheatTemperature: f32 @20; operatingKilowatts: f32 @24; minX: i32 @28; minY: i32 @32; maxX: i32 @36; maxY: i32 @40 |
| ModifyBuildingHeatExchangeMessage | 44 | callbackIdx: i32 @0; elemIdx: u16 @4; pad0: u8 @6; pad1: u8 @7; mass: f32 @8; temperature: f32 @12; thermalConductivity: f32 @16; overheatTemperature: f32 @20; operatingKilowatts: f32 @24; minX: i32 @28; minY: i32 @32; maxX: i32 @36; maxY: i32 @40 |
| ModifyBuildingEnergyMessage | 16 | handle: i32 @0; deltaKJ: f32 @4; minTemperature: f32 @8; maxTemperature: f32 @12 |
| RemoveBuildingHeatExchangeMessage | 8 | handle: i32 @0; callbackIdx: i32 @4 |
| RegisterBuildingToBuildingHeatExchangeMessage | 8 | callbackIdx: i32 @0; structureTemperatureHandler: i32 @4 |
| AddBuildingToBuildingHeatExchangeMessage | 12 | selfHandler: i32 @0; buildingInContactHandle: i32 @4; cellsInContact: i32 @8 |
| RemoveBuildingInContactFromBuildingToBuildingHeatExchangeMessage | 8 | selfHandler: i32 @0; buildingNoLongerInContactHandler: i32 @4 |
| RemoveBuildingToBuildingHeatExchangeMessage | 8 | callbackIdx: i32 @0; selfHandler: i32 @4 |
| AddDiseaseEmitterMessage | 4 | callbackIdx: i32 @0 |
| ModifyDiseaseEmitterMessage | 20 | handle: i32 @0; gameCell: i32 @4; diseaseIdx: u8 @8; maxDepth: u8 @9; pad0: u8 @10; pad1: u8 @11; emitInterval: f32 @12; emitCount: i32 @16 |
| RemoveDiseaseEmitterMessage | 8 | handle: i32 @0; callbackIdx: i32 @4 |
| SetSavedOptionsMessage | 2 | clearBits: u8 @0; setBits: u8 @1 |
| WorldOffsetData | 16 | worldOffsetX: i32 @0; worldOffsetY: i32 @4; worldSizeX: i32 @8; worldSizeY: i32 @12 |
| DigMessage | 12 | cellIdx: i32 @0; callbackIdx: i32 @4; skipEvent: bool8 @8; backwall: bool8 @9 |
| SetCellFloatValueMessage | 8 | cellIdx: i32 @0; value: f32 @4 |
| CellPropertiesMessage | 12 | cellIdx: i32 @0; callbackIdx: i32 @4; properties: u8 @8; set: u8 @9; pad0: u8 @10; pad1: u8 @11 |
| SetInsulationValueMessage | 8 | cellIdx: i32 @0; value: f32 @4 |
| ModifyCellMessage | 28 | cellIdx: i32 @0; callbackIdx: i32 @4; temperature: f32 @8; mass: f32 @12; diseaseCount: i32 @16; elementIdx: u16 @20; replaceType: u8 @22; diseaseIdx: u8 @23; addSubType: u8 @24 |
| CellDiseaseModification | 12 | cellIdx: i32 @0; diseaseIdx: u8 @4; pad0: u8 @5; pad1: u8 @6; pad2: u8 @7; diseaseCount: i32 @8 |
| RadiationParamsModification | 8 | RadiationParamsType: i32 @0; value: f32 @4 |
| CellRadiationModification | 12 | cellIdx: i32 @0; radiationDelta: f32 @4; callbackIdx: i32 @8 |
| MassConsumptionMessage | 16 | cellIdx: i32 @0; callbackIdx: i32 @4; mass: f32 @8; elementIdx: u16 @12; radius: u8 @14; height: u8 @15 |
| MassEmissionMessage | 24 | cellIdx: i32 @0; callbackIdx: i32 @4; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16; elementIdx: u16 @20; diseaseIdx: u8 @22 |
| ConsumeDiseaseMessage | 16 | gameCell: i32 @0; callbackIdx: i32 @4; percentToConsume: f32 @8; maxToConsume: i32 @12 |
| ModifyCellEnergyMessage | 16 | cellIdx: i32 @0; kilojoules: f32 @4; maxTemperature: f32 @8; id: i32 @12 |
| VisibleCells | 16 | min: i32[2] @0; max: i32[2] @8 |
| WakeCellMessage | 4 | gameCell: i32 @0 |
| ElementInteraction | 32 | interactionType: u32 @0; elemIdx1: u16 @4; elemIdx2: u16 @6; elemResultIdx: u16 @8; pad0: u8 @10; pad1: u8 @11; minMass: f32 @12; interactionProbability: f32 @16; elem1MassDestructionPercent: f32 @20; elem2MassRequiredMultiplier: f32 @24; elemResultMassCreationMultiplier: f32 @28 |
| CreateElementInteractionsMsg | 12 | numInteractions: i32 @0; interactions: ptr64<ElementInteraction> @4 |
| PipeChange | 20 | cell: i32 @0; layer: u8 @4; pad0: u8 @5; pad1: u8 @6; pad2: u8 @7; mass: f32 @8; temperature: f32 @12; elementHash: i32 @16 |
| CellWorldZoneModification | 8 | cell: i32 @0; zoneID: u8 @4 |
| SetBackwallDataMsg | 16 | gameCell: i32 @0; elementIdx: u16 @4; pad: u16 @6; mass: f32 @8; temperature: f32 @12 |
| Sim.NewGameFrame | 28 | elapsedSeconds: f32 @0; minX: i32 @4; minY: i32 @8; maxX: i32 @12; maxY: i32 @16; currentSunlightIntensity: f32 @20; currentCosmicRadiationIntensity: f32 @24 |
| Sim.Cell | 16 | elementIdx: u16 @0; properties: u8 @2; insulation: u8 @3; strengthInfo: u8 @4; pad0: u8 @5; pad1: u8 @6; pad2: u8 @7; temperature: f32 @8; mass: f32 @12 |
| Sim.DiseaseCell | 12 | diseaseIdx: u8 @0; reservedInfestationTickCount: u8 @1; pad1: u8 @2; pad2: u8 @3; elementCount: i32 @4; reservedAccumulatedError: f32 @8 |
| Sim.SimBackwall | 12 | elementIdx: u16 @0; pad0: u8 @2; pad1: u8 @3; mass: f32 @4; temperature: f32 @8 |
| Sim.Element | 164 | id: i32 @0; lowTempTransitionIdx: u16 @4; highTempTransitionIdx: u16 @6; elementsTableIdx: u16 @8; state: u8 @10; numberOfGradientColors: u8 @11; specificHeatCapacity: f32 @12; thermalConductivity: f32 @16; molarMass: f32 @20; solidSurfaceAreaMultiplier: f32 @24; liquidSurfaceAreaMultiplier: f32 @28; gasSurfaceAreaMultiplier: f32 @32; flow: f32 @36; viscosity: f32 @40; minHorizontalFlow: f32 @44; minVerticalFlow: f32 @48; maxMass: f32 @52; lowTemp: f32 @56; highTemp: f32 @60; strength: f32 @64; lowTempTransitionOreID: i32 @68; lowTempTransitionOreMassConversion: f32 @72; highTempTransitionOreID: i32 @76; highTempTransitionOreMassConversion: f32 @80; sublimateIndex: u16 @84; convertIndex: u16 @86; materialProperties: u32 @88; colour: u32 @92; gradientColours: u32[6] @96; sublimateFX: i32 @120; sublimateRate: f32 @124; sublimateEfficiency: f32 @128; sublimateProbability: f32 @132; offGasProbability: f32 @136; lightAbsorptionFactor: f32 @140; radiationAbsorptionFactor: f32 @144; radiationPer1000Mass: f32 @148; defaultValues: PhysicsData @152 |
| Sim.PhysicsData | 12 | temperature: f32 @0; mass: f32 @4; pressure: f32 @8 |
| Sim.DebugProperties | 12 | buildingTemperatureScale: f32 @0; buildingToBuildingTemperatureScale: f32 @4; isDebugEditing: u8 @8; pad0: u8 @9; pad1: u8 @10; pad2: u8 @11 |

## Output and callback contracts

SIM_Initialize receives GAME_MessageHandler(int messageId, IntPtr data) returning int. The managed handler handles diagnostic ID 0 (exception: callstack and dump filename pointers) and ID 1 (report: callstack/message/file pointers and i32 line). Gameplay callbacks are distinct descriptor records, not calls to this diagnostic delegate.

- Basic CallbackInfo carries an i32 callback handle, accumulated until World.UpdateCellInfo releases it. Manually released handles are filtered.
- ComponentStateChangedMessage carries callbackIdx and simHandle; the component callback manager releases the callback and passes the native handle to its managed caller.
- Mass-consumed and radiation-consumed managers release one-shot callbacks. Mass-emitted and disease-consumption managers use version-checked existing entries. These lifetimes are distinct.
- Registration/removal, emitter blocked/unblocked, consumed/emitted results, chunk melt, building overheat/recovery/melt, cell melt and backwall changes have separate consumers.

Native handles reserve -1 for invalid and -2 for queued registration. GetHandleIndex extracts the low 24 bits. Native handle generation/version allocation remains a separate requirement.

The declared GameDataUpdate layout computes to Pack=4, Linux x64, 496 bytes: frame count at 0; twelve grid/backwall pointers at 4..92; twenty-nine count/pointer spans at 100..447; accumulatedFlow at 448; five texture pointers at 456..488. Nonempty spans require valid native-owned arrays for the lifetime expected by managed consumers. Array production/reset/retirement needs native evidence.

| Output record | Size | Fields in declaration order |
| --- | ---: | --- |
| GameDataUpdate | 496 | numFramesProcessed: i32 @0; elementIdx: ptr64<ushort> @4; temperature: ptr64<float> @12; mass: ptr64<float> @20; properties: ptr64<byte> @28; insulation: ptr64<byte> @36; strengthInfo: ptr64<byte> @44; radiation: ptr64<float> @52; diseaseIdx: ptr64<byte> @60; diseaseCount: ptr64<int> @68; backwallElement: ptr64<ushort> @76; backwallMass: ptr64<float> @84; backwallTemperature: ptr64<float> @92; numSolidInfo: i32 @100; solidInfo: ptr64<SolidInfo> @104; numLiquidChangeInfo: i32 @112; liquidChangeInfo: ptr64<LiquidChangeInfo> @116; numSolidSubstanceChangeInfo: i32 @124; solidSubstanceChangeInfo: ptr64<SolidSubstanceChangeInfo> @128; numSubstanceChangeInfo: i32 @136; substanceChangeInfo: ptr64<SubstanceChangeInfo> @140; numCallbackInfo: i32 @148; callbackInfo: ptr64<CallbackInfo> @152; numSpawnFallingLiquidInfo: i32 @160; spawnFallingLiquidInfo: ptr64<SpawnFallingLiquidInfo> @164; numDigInfo: i32 @172; digInfo: ptr64<SpawnOreInfo> @176; numSpawnOreInfo: i32 @184; spawnOreInfo: ptr64<SpawnOreInfo> @188; numSpawnFXInfo: i32 @196; spawnFXInfo: ptr64<SpawnFXInfo> @200; numUnstableCellInfo: i32 @208; unstableCellInfo: ptr64<UnstableCellInfo> @212; numWorldDamageInfo: i32 @220; worldDamageInfo: ptr64<WorldDamageInfo> @224; numBuildingTemperatures: i32 @232; buildingTemperatures: ptr64<BuildingTemperatureInfo> @236; numMassConsumedCallbacks: i32 @244; massConsumedCallbacks: ptr64<MassConsumedCallback> @248; numMassEmittedCallbacks: i32 @256; massEmittedCallbacks: ptr64<MassEmittedCallback> @260; numDiseaseConsumptionCallbacks: i32 @268; diseaseConsumptionCallbacks: ptr64<DiseaseConsumptionCallback> @272; numComponentStateChangedMessages: i32 @280; componentStateChangedMessages: ptr64<ComponentStateChangedMessage> @284; numRemovedMassEntries: i32 @292; removedMassEntries: ptr64<ConsumedMassInfo> @296; numEmittedMassEntries: i32 @304; emittedMassEntries: ptr64<EmittedMassInfo> @308; numElementChunkInfos: i32 @316; elementChunkInfos: ptr64<ElementChunkInfo> @320; numElementChunkMeltedInfos: i32 @328; elementChunkMeltedInfos: ptr64<MeltedInfo> @332; numBuildingOverheatInfos: i32 @340; buildingOverheatInfos: ptr64<MeltedInfo> @344; numBuildingNoLongerOverheatedInfos: i32 @352; buildingNoLongerOverheatedInfos: ptr64<MeltedInfo> @356; numBuildingMeltedInfos: i32 @364; buildingMeltedInfos: ptr64<MeltedInfo> @368; numCellMeltedInfos: i32 @376; cellMeltedInfos: ptr64<CellMeltedInfo> @380; numBackwallElementChangedInfos: i32 @388; backwallElementChangedInfos: ptr64<BackwallElementChangedInfo> @392; numBackwallShouldTransitionInfos: i32 @400; backwallShouldTransitionInfos: ptr64<BackwallShouldTransitionInfo> @404; numDiseaseEmittedInfos: i32 @412; diseaseEmittedInfos: ptr64<DiseaseEmittedInfo> @416; numDiseaseConsumedInfos: i32 @424; diseaseConsumedInfos: ptr64<DiseaseConsumedInfo> @428; numRadiationConsumedCallbacks: i32 @436; radiationConsumedCallbacks: ptr64<ConsumedRadiationCallback> @440; accumulatedFlow: ptr64<float> @448; propertyTextureFlow: ptr64 @456; propertyTextureLiquid: ptr64 @464; propertyTextureLiquidData: ptr64 @472; propertyTextureMaterialData: ptr64 @480; propertyTextureExposedToSunlight: ptr64 @488 |
| DLLExceptionHandlerMessage | 16 | callstack: ptr64 @0; dmpFilename: ptr64 @8 |
| DLLReportMessageMessage | 28 | callstack: ptr64 @0; message: ptr64 @8; file: ptr64 @16; line: i32 @24 |
| SolidInfo | 8 | cellIdx: i32 @0; isSolid: i32 @4 |
| LiquidChangeInfo | 4 | cellIdx: i32 @0 |
| SolidSubstanceChangeInfo | 4 | cellIdx: i32 @0 |
| SubstanceChangeInfo | 8 | cellIdx: i32 @0; oldElemIdx: u16 @4; newElemIdx: u16 @6 |
| CallbackInfo | 4 | callbackIdx: i32 @0 |
| SpawnFallingLiquidInfo | 20 | cellIdx: i32 @0; elemIdx: u16 @4; diseaseIdx: u8 @6; pad0: u8 @7; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16 |
| SpawnOreInfo | 20 | cellIdx: i32 @0; elemIdx: u16 @4; diseaseIdx: u8 @6; pad0: u8 @7; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16 |
| SpawnFXInfo | 12 | cellIdx: i32 @0; fxHash: i32 @4; rotation: f32 @8 |
| UnstableCellInfo | 20 | cellIdx: i32 @0; elemIdx: u16 @4; fallingInfo: u8 @6; diseaseIdx: u8 @7; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16 |
| WorldDamageInfo | 8 | gameCell: i32 @0; damageSourceOffset: i32 @4 |
| PipeTemperatureChange | 8 | cellIdx: i32 @0; temperature: f32 @4 |
| MassConsumedCallback | 20 | callbackIdx: i32 @0; elemIdx: u16 @4; diseaseIdx: u8 @6; pad0: u8 @7; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16 |
| MassEmittedCallback | 20 | callbackIdx: i32 @0; elemIdx: u16 @4; suceeded: u8 @6; diseaseIdx: u8 @7; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16 |
| DiseaseConsumptionCallback | 12 | callbackIdx: i32 @0; diseaseIdx: u8 @4; pad0: u8 @5; pad1: u8 @6; pad2: u8 @7; diseaseCount: i32 @8 |
| ComponentStateChangedMessage | 8 | callbackIdx: i32 @0; simHandle: i32 @4 |
| EmittedMassInfo | 16 | elemIdx: u16 @0; diseaseIdx: u8 @2; pad0: u8 @3; mass: f32 @4; temperature: f32 @8; diseaseCount: i32 @12 |
| ConsumedMassInfo | 20 | simHandle: i32 @0; removedElemIdx: u16 @4; diseaseIdx: u8 @6; pad0: u8 @7; mass: f32 @8; temperature: f32 @12; diseaseCount: i32 @16 |
| ConsumedDiseaseInfo | 12 | simHandle: i32 @0; diseaseIdx: u8 @4; pad0: u8 @5; pad1: u8 @6; pad2: u8 @7; diseaseCount: i32 @8 |
| ElementChunkInfo | 8 | temperature: f32 @0; deltaKJ: f32 @4 |
| MeltedInfo | 4 | handle: i32 @0 |
| CellMeltedInfo | 4 | gameCell: i32 @0 |
| BackwallElementChangedInfo | 4 | gameCell: i32 @0 |
| BackwallShouldTransitionInfo | 4 | gameCell: i32 @0 |
| BuildingTemperatureInfo | 8 | handle: i32 @0; temperature: f32 @4 |
| BuildingConductivityData | 12 | temperature: f32 @0; heatCapacity: f32 @4; thermalConductivity: f32 @8 |
| DiseaseEmittedInfo | 8 | diseaseIdx: u8 @0; pad0: u8 @1; pad1: u8 @2; pad2: u8 @3; count: i32 @4 |
| DiseaseConsumedInfo | 8 | diseaseIdx: u8 @0; pad0: u8 @1; pad1: u8 @2; pad2: u8 @3; count: i32 @4 |
| ConsumedRadiationCallback | 12 | callbackIdx: i32 @0; gameCell: i32 @4; radiation: f32 @8 |

## Exported managed signatures and save boundary

The managed imports establish these signatures. They are declarations, not implementation source.

```csharp
void SIM_Initialize(GAME_MessageHandler callback);
void SIM_Shutdown();
IntPtr SIM_HandleMessage(int sim_msg_id, int msg_length, byte* msg);
IntPtr SIM_HandleMessages(int sim_msg_id, int msg_length, int msg_count, byte* msg);
byte* SIM_BeginSave(int* size, int x, int y);
void SIM_EndSave();
void SIM_DebugCrash();
char* SYSINFO_Acquire();
void SYSINFO_Release();
```

Sim.Save copies the BeginSave pointer to managed bytes, ends the native save, then writes i32 count and bytes. Load/LoadWorld read that count and submit only the blob to Load; null return means failure. This establishes an outer envelope, not the native save format, versions, offsets, RNG, components, compression or ownership internals. Official save byte compatibility is unresolved.

## SimMessages method signatures

This is the complete targeted type inventory. Convenience wrappers reuse messages; signatures do not imply behavior coverage. The private UTF-8 helper is included. No method bodies are reproduced.

```csharp
public unsafe static void AddElementConsumer(int gameCell, ElementConsumer.Configuration configuration, SimHashes element, byte radius, int cb_handle);
public unsafe static void SetElementConsumerData(int sim_handle, int cell, float consumptionRate);
public unsafe static void RemoveElementConsumer(int cb_handle, int sim_handle);
public unsafe static void AddElementEmitter(float max_pressure, int on_registered, int on_blocked = -1, int on_unblocked = -1);
public unsafe static void ModifyElementEmitter(int sim_handle, int game_cell, int max_depth, SimHashes element, float emit_interval, float emit_mass, float emit_temperature, float max_pressure, byte disease_idx, int disease_count);
public unsafe static void RemoveElementEmitter(int cb_handle, int sim_handle);
public unsafe static void AddRadiationEmitter(int on_registered, int game_cell, short emitRadiusX, short emitRadiusY, float emitRads, float emitRate, float emitSpeed, float emitDirection, float emitAngle, RadiationEmitter.RadiationEmitterType emitType);
public unsafe static void ModifyRadiationEmitter(int sim_handle, int game_cell, short emitRadiusX, short emitRadiusY, float emitRads, float emitRate, float emitSpeed, float emitDirection, float emitAngle, RadiationEmitter.RadiationEmitterType emitType);
public unsafe static void RemoveRadiationEmitter(int cb_handle, int sim_handle);
public unsafe static void AddElementChunk(int gameCell, SimHashes element, float mass, float temperature, float surface_area, float thickness, float ground_transfer_scale, int cb_handle);
public unsafe static void RemoveElementChunk(int sim_handle, int cb_handle);
public unsafe static void SetElementChunkData(int sim_handle, float temperature, float heat_capacity);
public unsafe static void MoveElementChunk(int sim_handle, int cell);
public unsafe static void ModifyElementChunkEnergy(int sim_handle, float delta_kj);
public unsafe static void ModifyElementChunkTemperatureAdjuster(int sim_handle, float temperature, float heat_capacity, float thermal_conductivity);
public unsafe static void AddBuildingHeatExchange(Extents extents, float mass, float temperature, float thermal_conductivity, float operating_kw, ushort elem_idx, int callbackIdx = -1);
public unsafe static void ModifyBuildingHeatExchange(int sim_handle, Extents extents, float mass, float temperature, float thermal_conductivity, float overheat_temperature, float operating_kw, ushort element_idx);
public unsafe static void RemoveBuildingHeatExchange(int sim_handle, int callbackIdx = -1);
public unsafe static void ModifyBuildingEnergy(int sim_handle, float delta_kj, float min_temperature, float max_temperature);
public unsafe static void RegisterBuildingToBuildingHeatExchange(int structureTemperatureHandler, int callbackIdx = -1);
public unsafe static void AddBuildingToBuildingHeatExchange(int selfHandler, int buildingInContact, int cellsInContact);
public unsafe static void RemoveBuildingInContactFromBuildingToBuildingHeatExchange(int selfHandler, int buildingToRemove);
public unsafe static void RemoveBuildingToBuildingHeatExchange(int selfHandler, int callback = -1);
public unsafe static void AddDiseaseEmitter(int callbackIdx);
public unsafe static void ModifyDiseaseEmitter(int sim_handle, int cell, byte range, byte disease_idx, float emit_interval, int emit_count);
public unsafe static void RemoveDiseaseEmitter(int cb_handle, int sim_handle);
public unsafe static void SetSavedOptionValue(SimSavedOptions option, int zero_or_one);
private static void WriteKleiString(this BinaryWriter writer, string str);
public unsafe static void CreateSimElementsTable(List<Element> elements);
public unsafe static void CreateDiseaseTable(Diseases diseases);
public unsafe static void DefineWorldOffsets(List<WorldOffsetData> worldOffsets);
public static void SimDataInitializeFromCells(int width, int height, uint simSeed, ref WorldgenSimData simData, bool headless);
public static void SimDataResizeGridAndInitializeVacuumCells(Vector2I grid_size, int width, int height, int x_offset, int y_offset);
public static void SimDataFreeCells(int width, int height, int x_offset, int y_offset);
public unsafe static void Dig(int gameCell, int callbackIdx = -1, bool skipEvent = false, bool backwall = false);
public unsafe static void SetInsulation(int gameCell, float value);
public unsafe static void SetStrength(int gameCell, int weight, float strengthMultiplier);
public unsafe static void SetCellProperties(int gameCell, byte properties, int callbackIdx = -1);
public unsafe static void ClearCellProperties(int gameCell, byte properties, int callbackIdx = -1);
public unsafe static void ModifyCell(int gameCell, ushort elementIdx, float temperature, float mass, byte disease_idx, int disease_count, ReplaceType replace_type = ReplaceType.None, bool do_vertical_solid_displacement = false, int callbackIdx = -1);
public unsafe static void ModifyDiseaseOnCell(int gameCell, byte disease_idx, int disease_delta);
public unsafe static void ModifyRadiationOnCell(int gameCell, float radiationDelta, int callbackIdx = -1);
public unsafe static void ModifyRadiationParams(RadiationParams type, float value);
public static ushort GetElementIndex(SimHashes element);
public unsafe static void ConsumeMass(int gameCell, SimHashes element, float mass, byte radius, int callbackIdx = -1);
public unsafe static void ConsumeMass(int gameCell, SimHashes element, float mass, byte width, byte height, int callbackIdx = -1);
public unsafe static void EmitMass(int gameCell, ushort element_idx, float mass, float temperature, byte disease_idx, int disease_count, int callbackIdx = -1);
public unsafe static void ConsumeDisease(int game_cell, float percent_to_consume, int max_to_consume, int callback_idx);
public static void AddRemoveSubstance(int gameCell, SimHashes new_element, CellAddRemoveSubstanceEvent ev, float mass, float temperature, byte disease_idx, int disease_count, bool do_vertical_solid_displacement = true, int callbackIdx = -1);
public static void AddRemoveSubstance(int gameCell, ushort elementIdx, CellAddRemoveSubstanceEvent ev, float mass, float temperature, byte disease_idx, int disease_count, bool do_vertical_solid_displacement = true, int callbackIdx = -1);
public static void ReplaceElement(int gameCell, SimHashes new_element, CellElementEvent ev, float mass, float temperature = -1f, byte diseaseIdx = byte.MaxValue, int diseaseCount = 0, int callbackIdx = -1);
public static void ReplaceAndDisplaceElement(int gameCell, SimHashes new_element, CellElementEvent ev, float mass, float temperature = -1f, byte disease_idx = byte.MaxValue, int disease_count = 0, int callbackIdx = -1);
public unsafe static void ModifyEnergy(int gameCell, float kilojoules, float max_temperature, EnergySourceID id);
public static void ModifyMass(int gameCell, float mass, byte disease_idx, int disease_count, CellModifyMassEvent ev, float temperature = -1f, SimHashes element = SimHashes.Vacuum);
public unsafe static void CreateElementInteractions(ElementInteraction[] interactions);
public unsafe static void NewGameFrame(float elapsed_seconds, List<Game.SimActiveRegion> activeRegions);
public unsafe static void SetDebugProperties(Sim.DebugProperties properties);
public unsafe static void ModifyCellWorldZone(int cell, byte zone_id);
public unsafe static void SetBackwallData(int cell, ushort elemIdx, float mass, float temperature);
```

## Static source locations

Transient evidence files and relevant positions (not repository source):

| Evidence | Transient file and lines |
| --- | --- |
| All 62 IDs | catalogue-SimMessageHashes.cs:1-65 |
| All SimMessages wrappers and input structs | catalogue-SimMessages.cs:1-1539 |
| Frame fields / exports / startup result binding | catalogue-Sim.cs:693-709 / 1017-1035 / 1102-1122 |
| Frame submission and PrepareGameData | catalogue-Game.cs:1133-1446 / 1533-1574 |
| Existing-save / worldgen startup | catalogue-SaveLoader.cs:428-468 / 1096-1130 |
| Basic callbacks released after cell updates | catalogue-World.cs:63-90 |
| Raw per-cell world zones | catalogue-SubworldZoneRenderData.cs:230-324 |
| Empty profiler message | catalogue-DebugHandler.cs:411-413 |

The five retained enums with unknown payloads have no direct ldc.i4 sender in this official assembly inspection. This is a bounded negative result, not a proof that no native or dynamically constructed caller exists.

## Remaining native requirements

- Establish per-ID queueing, input copying, dispatch order, zero-dt behavior, worker scheduling and PrepareGameData result retirement.
- Resolve retained enums with no observed sender; do not report unsupported commands as successful no-ops.
- Implement pressure/mass/energy, gas/liquid/solid movement and displacement, transitions, sublimation, world boundaries, disease, radiation, buildings and chunks using supplied tables.
- Produce coherent output spans, flow/textures, handles and callbacks with explicit FFI bounds and ownership.
- Decode official save/load and multiple-world assembly before claiming normal game startup compatibility.
- Compile-time layout checks are static evidence only. Runtime, official conformance, game/save/mod acceptance and performance remain deferred under the user instruction to finish development before execution.
