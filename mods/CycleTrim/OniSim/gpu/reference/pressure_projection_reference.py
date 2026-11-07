"""Cell-centred reference for a variable-density, closed-domain pressure projection.

This is a numerical-method reference, not a port of OniSim's legacy pressure
semantics. Arrays are row-major (y, x); face-normal velocities live on faces.
Only the Python standard library is required.
"""
from __future__ import annotations

from dataclasses import dataclass
from math import sqrt
from typing import Sequence


@dataclass
class ProjectionResult:
    pressure: list[float]
    u_faces: list[float]       # height * (width + 1)
    v_faces: list[float]       # (height + 1) * width
    divergence_before: float
    divergence_after: float
    relative_residual: float
    iterations: int


def _l2(values: Sequence[float], solid: Sequence[bool]) -> float:
    return sqrt(sum(v * v for v, wall in zip(values, solid) if not wall))


def project(
    width: int,
    height: int,
    rho: Sequence[float],
    ux: Sequence[float],
    uy: Sequence[float],
    solid: Sequence[bool],
    dt: float,
    *,
    dx: float = 1.0,
    dy: float = 1.0,
    max_iterations: int = 4000,
    tolerance: float = 1.0e-7,
    omega: float = 1.0,
) -> ProjectionResult:
    """Project cell-centred provisional velocity onto a divergence-free field.

    Discretization:
      div(beta grad(p)) = div(u*) / dt, beta = 1 / rho_face
      u = u* - dt * beta * grad(p)

    Closed outer walls and solid-fluid faces have zero normal velocity.
    Face density is the arithmetic mean of adjacent cell densities. Neumann
    pressure boundaries are used; pressure is gauge-fixed to zero mean.
    """
    n = width * height
    if width < 2 or height < 2 or len(rho) != n or len(ux) != n or len(uy) != n or len(solid) != n:
        raise ValueError("grid and field dimensions must match and be at least 2x2")
    if not (dt > 0.0 and dx > 0.0 and dy > 0.0 and max_iterations > 0 and tolerance > 0.0):
        raise ValueError("dt, spacing, iteration limit and tolerance must be positive")
    if not (0.0 < omega < 2.0):
        raise ValueError("RBGS relaxation factor must lie in (0, 2)")
    for i in range(n):
        if not (rho[i] > 0.0) or not all(map(_finite, (rho[i], ux[i], uy[i]))):
            raise ValueError("density must be positive and all fields finite")

    u_width = width + 1
    v_height = height + 1
    u = [0.0] * (height * u_width)
    v = [0.0] * (v_height * width)
    beta_u = [0.0] * len(u)
    beta_v = [0.0] * len(v)

    # Build shared face values once. This avoids per-cell disagreement between
    # the two cells sharing a face and makes the finite-volume divergence sum
    # telescope exactly up to floating-point accumulation.
    for y in range(height):
        for xf in range(1, width):
            left = y * width + xf - 1
            right = left + 1
            fi = y * u_width + xf
            if not solid[left] and not solid[right]:
                u[fi] = 0.5 * (ux[left] + ux[right])
                beta_u[fi] = 2.0 / (rho[left] + rho[right])
    for yf in range(1, height):
        for x in range(width):
            top = (yf - 1) * width + x
            bottom = top + width
            fi = yf * width + x
            if not solid[top] and not solid[bottom]:
                v[fi] = 0.5 * (uy[top] + uy[bottom])
                beta_v[fi] = 2.0 / (rho[top] + rho[bottom])

    def divergence() -> list[float]:
        out = [0.0] * n
        for y in range(height):
            for x in range(width):
                i = y * width + x
                if solid[i]:
                    continue
                out[i] = (
                    (u[y * u_width + x + 1] - u[y * u_width + x]) / dx
                    + (v[(y + 1) * width + x] - v[y * width + x]) / dy
                )
        return out

    rhs = [d / dt for d in divergence()]
    fluid = [i for i in range(n) if not solid[i]]
    if not fluid:
        raise ValueError("domain contains no fluid cells")
    rhs_mean = sum(rhs[i] for i in fluid) / len(fluid)
    # A closed, shared-face finite-volume divergence has zero global sum.
    # Remove only accumulated roundoff; a larger incompatibility is a bug.
    rhs_scale = max(1.0, max(abs(rhs[i]) for i in fluid))
    if abs(rhs_mean) > 1.0e-10 * rhs_scale:
        raise ValueError("closed-domain Poisson RHS is not mean-zero")
    for i in fluid:
        rhs[i] -= rhs_mean

    pressure = [0.0] * n
    inv_dx2 = 1.0 / (dx * dx)
    inv_dy2 = 1.0 / (dy * dy)

    def stencil(i: int):
        y, x = divmod(i, width)
        entries = []
        if x > 0:
            fi = y * u_width + x
            if beta_u[fi] > 0.0:
                entries.append((i - 1, beta_u[fi] * inv_dx2))
        if x + 1 < width:
            fi = y * u_width + x + 1
            if beta_u[fi] > 0.0:
                entries.append((i + 1, beta_u[fi] * inv_dx2))
        if y > 0:
            fi = y * width + x
            if beta_v[fi] > 0.0:
                entries.append((i - width, beta_v[fi] * inv_dy2))
        if y + 1 < height:
            fi = (y + 1) * width + x
            if beta_v[fi] > 0.0:
                entries.append((i + width, beta_v[fi] * inv_dy2))
        return entries

    stencils = [stencil(i) if not solid[i] else [] for i in range(n)]
    iterations = 0
    for iteration in range(max_iterations):
        for color in (0, 1):
            for y in range(height):
                for x in range(width):
                    if ((x + y) & 1) != color:
                        continue
                    i = y * width + x
                    if solid[i]:
                        continue
                    entries = stencils[i]
                    diagonal = sum(weight for _, weight in entries)
                    if diagonal == 0.0:
                        pressure[i] = 0.0
                        continue
                    gs = (sum(weight * pressure[j] for j, weight in entries) - rhs[i]) / diagonal
                    pressure[i] += omega * (gs - pressure[i])
        iterations = iteration + 1
        mean_p = sum(pressure[i] for i in fluid) / len(fluid)
        for i in fluid:
            pressure[i] -= mean_p
        residual = _residual(pressure, rhs, stencils, solid)
        if residual <= tolerance:
            break

    # Apply the pressure gradient on the exact same shared faces used above.
    for y in range(height):
        for xf in range(1, width):
            fi = y * u_width + xf
            if beta_u[fi] == 0.0:
                u[fi] = 0.0
                continue
            left = y * width + xf - 1
            right = left + 1
            u[fi] -= dt * beta_u[fi] * (pressure[right] - pressure[left]) / dx
    for yf in range(1, height):
        for x in range(width):
            fi = yf * width + x
            if beta_v[fi] == 0.0:
                v[fi] = 0.0
                continue
            top = (yf - 1) * width + x
            bottom = top + width
            v[fi] -= dt * beta_v[fi] * (pressure[bottom] - pressure[top]) / dy

    before = _l2(rhs, solid) * dt
    after = _l2(divergence(), solid)
    final_residual = _residual(pressure, rhs, stencils, solid)
    return ProjectionResult(pressure, u, v, before, after, final_residual, iterations)


def _residual(pressure, rhs, stencils, solid):
    residual = []
    for i, wall in enumerate(solid):
        if wall:
            residual.append(0.0)
            continue
        laplace = sum(weight * (pressure[j] - pressure[i]) for j, weight in stencils[i])
        residual.append(laplace - rhs[i])
    numerator = _l2(residual, solid)
    denominator = max(_l2(rhs, solid), 1.0e-30)
    return numerator / denominator


def _finite(value: float) -> bool:
    return value == value and abs(value) != float("inf")
