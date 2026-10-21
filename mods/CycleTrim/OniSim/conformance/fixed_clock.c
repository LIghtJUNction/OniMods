/* Fixture-only realtime seed control. Never loaded into an ONI game process. */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <errno.h>
#include <stdatomic.h>
#include <stdint.h>
#include <time.h>

#define FIXTURE_SECONDS ((time_t)1900000000)
static _Atomic uint64_t time_calls;
static _Atomic uint64_t realtime_calls;

time_t time(time_t *result) {
    atomic_fetch_add_explicit(&time_calls, 1, memory_order_relaxed);
    if (result != NULL) *result = FIXTURE_SECONDS;
    return FIXTURE_SECONDS;
}

int clock_gettime(clockid_t clock, struct timespec *result) {
    if (clock == CLOCK_REALTIME || clock == CLOCK_REALTIME_COARSE) {
        atomic_fetch_add_explicit(&realtime_calls, 1, memory_order_relaxed);
        result->tv_sec = FIXTURE_SECONDS;
        result->tv_nsec = 0;
        return 0;
    }
    int (*original)(clockid_t, struct timespec *) = dlsym(RTLD_NEXT, "clock_gettime");
    if (original == NULL) {
        errno = ENOSYS;
        return -1;
    }
    return original(clock, result);
}

uint64_t CycleTrimFixture_TimeCalls(void) {
    return atomic_load_explicit(&time_calls, memory_order_relaxed);
}

uint64_t CycleTrimFixture_RealtimeCalls(void) {
    return atomic_load_explicit(&realtime_calls, memory_order_relaxed);
}
