import unittest

from pressure_projection_reference import project


class PressureProjectionTests(unittest.TestCase):
    def fixture(self, width=24, height=18):
        n = width * height
        solid = [False] * n
        for x in range(width):
            solid[x] = solid[(height - 1) * width + x] = True
        for y in range(height):
            solid[y * width] = solid[y * width + width - 1] = True
        for y in range(height // 3, height // 3 + 3):
            for x in range(width // 2, width // 2 + 2):
                solid[y * width + x] = True
        rho = [1.0 + (i % 7) * 0.1 for i in range(n)]
        ux = [((i * 17) % 29 - 14) * 0.003 for i in range(n)]
        uy = [((i * 11) % 23 - 11) * 0.004 for i in range(n)]
        return width, height, rho, ux, uy, solid

    def test_projection_reduces_divergence(self):
        args = self.fixture()
        result = project(*args, 0.2, max_iterations=4000, tolerance=1e-8)
        self.assertLess(result.divergence_after, result.divergence_before * 1e-5)
        self.assertLess(result.relative_residual, 1e-7)

    def test_outer_and_solid_faces_have_zero_normal_velocity(self):
        width, height, rho, ux, uy, solid = self.fixture()
        result = project(width, height, rho, ux, uy, solid, 0.2)
        uw = width + 1
        for y in range(height):
            self.assertEqual(result.u_faces[y * uw], 0.0)
            self.assertEqual(result.u_faces[y * uw + width], 0.0)
            for xf in range(1, width):
                left, right = y * width + xf - 1, y * width + xf
                if solid[left] or solid[right]:
                    self.assertEqual(result.u_faces[y * uw + xf], 0.0)
        for yf in range(height + 1):
            for x in range(width):
                if yf == 0 or yf == height:
                    self.assertEqual(result.v_faces[yf * width + x], 0.0)
                elif solid[(yf - 1) * width + x] or solid[yf * width + x]:
                    self.assertEqual(result.v_faces[yf * width + x], 0.0)

    def test_invalid_density_is_rejected(self):
        width, height, rho, ux, uy, solid = self.fixture()
        rho[width + 1] = 0.0
        with self.assertRaises(ValueError):
            project(width, height, rho, ux, uy, solid, 0.2)

    def test_uniform_zero_velocity_is_fixed_point(self):
        width, height, rho, _, _, solid = self.fixture()
        n = width * height
        result = project(width, height, rho, [0.0] * n, [0.0] * n, solid, 0.2)
        self.assertEqual(result.divergence_before, 0.0)
        self.assertEqual(result.divergence_after, 0.0)
        self.assertTrue(all(p == 0.0 for p in result.pressure))


if __name__ == "__main__":
    unittest.main()
