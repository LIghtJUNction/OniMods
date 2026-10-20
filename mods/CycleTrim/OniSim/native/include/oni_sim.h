#ifndef ONI_SIM_H
#define ONI_SIM_H

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/* Experimental Linux x64 boundary. These declarations do not establish game
 * compatibility. Check ONISIM_GetCapabilities before choosing an operation. */
#if UINTPTR_MAX != UINT64_MAX
#error OniSim currently defines only the 64-bit pointer layout
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef int32_t (*OniSimMessageHandler)(int32_t message_id, const void *data);

/* Official export shapes, recovered from the installed managed assembly.
 * Payload scalars are little endian. HandleMessages length is the per-message
 * stride, not the combined buffer length; its messages share one id. */
/* The official initialization resets an existing simulation, as required by the
 * game's load path. Use ONISIM_TryInitialize for exclusive session acquisition.
 * Successful shutdown and generation-exhaustion status 4 both release storage;
 * internal failure status 5 does not prove cleanup. */
void SIM_Initialize(OniSimMessageHandler callback);
void SIM_Shutdown(void);
void *SIM_HandleMessage(int32_t id, int32_t length, const uint8_t *message);
void *SIM_HandleMessages(int32_t id, int32_t length, int32_t count,
                         const uint8_t *messages);
const uint8_t *SIM_BeginSave(int32_t *size, int32_t x, int32_t y);
void SIM_EndSave(void);
void SIM_DebugCrash(void);
const char *SYSINFO_Acquire(void);
void SYSINFO_Release(void);

enum OniSimStatus {
    ONISIM_SUCCESS = 0,
    ONISIM_INVALID_ARGUMENT = 1,
    ONISIM_UNSUPPORTED = 2,
    ONISIM_INVALID_STATE = 3,
    ONISIM_RESOURCE_LIMIT = 4,
    ONISIM_INTERNAL_FAILURE = 5,
    ONISIM_BUFFER_TOO_SMALL = 6
};

/* Project extensions. There is no OSB1 envelope or request/response framing.
 * Check status immediately after each operation, before calling another one.
 * GetLastStatus/GetLastError themselves do not clear the thread-local error.
 * A returned error string lasts until the next operation on the same thread. */
uint32_t ONISIM_GetAbiVersion(void);
/* Refuses an existing session without resetting it. Rejected acquisition
 * (status 3 or 4) does not transfer ownership; do not shut down that session.
 * Check status immediately, before calling any other extension. */
void ONISIM_TryInitialize(OniSimMessageHandler callback);
int32_t ONISIM_GetLastStatus(void);
const char *ONISIM_GetLastError(void);
uint64_t ONISIM_GetGeneration(void);
const char *ONISIM_GetCapabilities(void);
const uint8_t *ONISIM_GetGrid(void);

/* Explicit snapshot using the official InitializeFromCells image layout.
 * A NULL destination or zero capacity returns the required byte count without
 * changing simulation state. A negative capacity is always an error.
 * Success returns the written byte count; errors return -1 and set status.
 * An undersized destination is not written. Callers own their output buffers. */
int32_t ONISIM_CopyGridImage(uint8_t *destination, int32_t capacity);

/* Pack=4, Linux x64 GameDataUpdate. Array pointers are native-owned, borrowed
 * only while the session is alive and its grid generation remains current.
 * Event and texture pointers belong to one publication and must be consumed
 * before the next Prepare. External reads must not overlap mutating calls.
 * No caller may write through these pointers. */
#pragma pack(push, 4)
typedef struct OniSimEventSpan {
    int32_t count;
    const void *data;
} OniSimEventSpan;

typedef struct OniSimGameDataUpdate {
    int32_t num_frames_processed;
    const uint16_t *element;
    const float *temperature;
    const float *mass;
    const uint8_t *properties;
    const uint8_t *insulation;
    const uint8_t *strength;
    const float *radiation;
    const uint8_t *disease;
    const int32_t *disease_count;
    const uint16_t *backwall_element;
    const float *backwall_mass;
    const float *backwall_temperature;
    OniSimEventSpan events[29];
    const void *accumulated_flow;
    const void *texture_flow;
    const void *texture_liquid;
    const void *texture_liquid_data;
    const void *texture_material_data;
    const void *texture_exposed_to_sunlight;
} OniSimGameDataUpdate;

typedef struct OniSimBuildingTemperatureInfo {
    int32_t handle;
    float temperature;
} OniSimBuildingTemperatureInfo;

/* Temperatures are indexed by the low 24 bits of the conduit handle. Frozen
 * and melted entries contain those slot indices, not full generation handles.
 * All three arrays and this descriptor are native-owned. Consume them before
 * the next conduit mutation or shutdown, with no concurrent native writes. */
typedef struct OniSimConduitTemperatureUpdate {
    int32_t temperature_count;
    const float *temperatures;
    int32_t frozen_count;
    const int32_t *frozen;
    int32_t melted_count;
    const int32_t *melted;
} OniSimConduitTemperatureUpdate;
#pragma pack(pop)

void ConduitTemperatureManager_Initialize(void);
void ConduitTemperatureManager_Shutdown(void);
int32_t ConduitTemperatureManager_Add(
    float temperature, float mass, int32_t contents_element_hash,
    int32_t structure_temperature_handle, float conduit_heat_capacity,
    float conduit_thermal_conductivity, bool insulated);
/* The official managed declaration returns int32_t and ignores the value;
 * the native implementation has no defined return value. This implementation
 * returns zero on success, -1 on failure; check ONISIM_GetLastStatus. */
int32_t ConduitTemperatureManager_Set(int32_t handle, float temperature,
                                    float mass, int32_t contents_element_hash);
void ConduitTemperatureManager_Remove(int32_t handle);
void ConduitTemperatureManager_Clear(void);
const OniSimConduitTemperatureUpdate *ConduitTemperatureManager_Update(
    float dt, const OniSimBuildingTemperatureInfo *building_temperatures);

#ifdef __cplusplus
}
static_assert(sizeof(OniSimEventSpan) == 12, "OniSim event span layout");
static_assert(sizeof(OniSimGameDataUpdate) == 496, "OniSim grid layout");
static_assert(sizeof(OniSimBuildingTemperatureInfo) == 8, "Building heat layout");
static_assert(sizeof(OniSimConduitTemperatureUpdate) == 36, "Conduit layout");
static_assert(offsetof(OniSimConduitTemperatureUpdate, temperatures) == 4,
              "Conduit temperature pointer offset");
#elif defined(__STDC_VERSION__) && __STDC_VERSION__ >= 201112L
_Static_assert(sizeof(OniSimEventSpan) == 12, "OniSim event span layout");
_Static_assert(sizeof(OniSimGameDataUpdate) == 496, "OniSim grid layout");
_Static_assert(sizeof(OniSimBuildingTemperatureInfo) == 8, "Building heat layout");
_Static_assert(sizeof(OniSimConduitTemperatureUpdate) == 36, "Conduit layout");
_Static_assert(offsetof(OniSimConduitTemperatureUpdate, temperatures) == 4,
               "Conduit temperature pointer offset");
#endif

#endif
