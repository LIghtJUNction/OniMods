#!/usr/bin/env python3
"""Deterministic CPU reference and correctness/performance gate for the WGSL flux operator."""
from __future__ import annotations
import argparse, json, statistics, time
import numpy as np

def step(mass, ux, uy, drift_y, solid, dt):
    h, w, channels = mass.shape
    out = mass.copy()
    if w > 1:
        speed = 0.5 * (ux[:, :-1] + ux[:, 1:])
        active = (~solid[:, :-1]) & (~solid[:, 1:])
        a, b = mass[:, :-1], mass[:, 1:]
        flux = speed[..., None] * np.where(speed[..., None] >= 0, a, b)
        flux *= active[..., None]
        out[:, :-1] -= dt * flux
        out[:, 1:] += dt * flux
    if h > 1:
        speed = 0.5 * (uy[:-1] + uy[1:])[..., None] + drift_y[None, None, :]
        active = (~solid[:-1]) & (~solid[1:])
        a, b = mass[:-1], mass[1:]
        flux = speed * np.where(speed >= 0, a, b)
        flux *= active[..., None]
        out[:-1] -= dt * flux
        out[1:] += dt * flux
    out[solid] = 0.0
    return out

def fixture(h, w, channels, seed):
    rng = np.random.default_rng(seed)
    solid = np.zeros((h, w), dtype=bool)
    solid[[0, -1], :] = True
    solid[:, [0, -1]] = True
    solid[h//3: h//3+9, w//3: w//3+13] = True
    mass = rng.random((h, w, channels), dtype=np.float32) * 0.25
    mass[solid] = 0
    ux = rng.uniform(-0.02, 0.02, (h, w)).astype(np.float32)
    uy = rng.uniform(-0.02, 0.02, (h, w)).astype(np.float32)
    drift = np.linspace(-0.01, 0.01, channels, dtype=np.float32)
    return mass, ux, uy, drift, solid

def run_case(h, w, channels, steps=100):
    state = fixture(h, w, channels, 20261107)
    mass, ux, uy, drift, solid = state
    initial = mass.sum(axis=(0, 1), dtype=np.float64)
    max_speed = max(float(np.max(np.abs(ux))) + float(np.max(np.abs(drift))),
                    float(np.max(np.abs(uy))) + float(np.max(np.abs(drift))))
    dt = 0.2 / max(max_speed, 1e-12)  # conservative 1-D CFL bound
    for _ in range(steps):
        mass = step(mass, ux, uy, drift, solid, dt)
    final = mass.sum(axis=(0, 1), dtype=np.float64)
    rel = np.abs(final - initial) / np.maximum(np.abs(initial), 1e-30)
    assert float(rel.max()) < 2e-6, f"mass conservation failed: {rel}"
    assert float(mass.min()) >= -1e-7, f"negative mass: {mass.min()}"
    assert float(np.abs(mass[solid]).max()) == 0.0, "mass entered solid cells"
    return {"shape": [h, w, channels], "steps": steps, "dt": dt,
            "max_relative_mass_error": float(rel.max()),
            "minimum_mass": float(mass.min()), "solid_mass": float(mass[solid].sum())}

def benchmark(h=384, w=256, channels=4, samples=31):
    mass, ux, uy, drift, solid = fixture(h, w, channels, 20261107)
    max_speed = max(float(np.max(np.abs(ux))) + float(np.max(np.abs(drift))),
                    float(np.max(np.abs(uy))) + float(np.max(np.abs(drift))))
    dt = 0.2 / max(max_speed, 1e-12)
    for _ in range(5):
        step(mass, ux, uy, drift, solid, dt)
    timings = []
    for _ in range(samples):
        start = time.perf_counter_ns()
        step(mass, ux, uy, drift, solid, dt)
        timings.append((time.perf_counter_ns() - start) / 1000.0)
    ordered = sorted(timings)
    return {"device": "CPU / NumPy (GPU not measured)",
            "shape": [h, w, channels], "samples": samples, "warmup": 5,
            "min_us": min(timings), "median_us": statistics.median(timings),
            "p95_us": float(np.percentile(ordered, 95)), "max_us": max(timings),
            "steps_per_second": 1e6 / statistics.median(timings)}

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", default=None)
    args = parser.parse_args()
    results = [run_case(64, 96, 4), run_case(384, 256, 4), run_case(128, 128, 16)]
    report = {"operator": "conservative multi-component finite-volume advection",
              "correctness": results, "benchmark": benchmark()}
    rendered = json.dumps(report, indent=2)
    print(rendered)
    if args.report:
        with open(args.report, "x", encoding="utf-8") as f:
            f.write(rendered + "\n")
