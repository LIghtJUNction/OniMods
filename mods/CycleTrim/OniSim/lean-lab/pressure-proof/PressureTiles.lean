import Std

/-! Fixed 32-wave by 16-row skew tiles. This models dependencies, not Rust
scratch ownership, copying, notification merging or machine arithmetic. -/
namespace PressureTiles

structure Cell where
  x : Int
  y : Int

def Touch (p r : Cell) : Prop :=
  p.x ≤ r.x ∧ r.x ≤ p.x + 1 ∧ p.y ≤ r.y ∧ r.y ≤ p.y + 1

def Before (p q : Cell) : Prop := p.y < q.y ∨ (p.y = q.y ∧ p.x < q.x)
def band (p : Cell) : Int := (p.x + 2 * p.y) / 32
def rows (p : Cell) : Int := p.y / 16
def level (p : Cell) : Int := band p + rows p

theorem overlapping_order_monotone_tiles (p q r : Cell)
    (hp : Touch p r) (hq : Touch q r) (order : Before p q) :
    band p ≤ band q ∧ rows p ≤ rows q := by
  unfold Touch at hp hq
  unfold Before at order
  unfold band rows
  omega

theorem distinct_overlapping_tiles_increase_level (p q r : Cell)
    (hp : Touch p r) (hq : Touch q r) (order : Before p q)
    (different : band p ≠ band q ∨ rows p ≠ rows q) : level p < level q := by
  have monotone := overlapping_order_monotone_tiles p q r hp hq order
  unfold level
  omega

theorem same_level_overlapping_tasks_share_tile (p q r : Cell)
    (hp : Touch p r) (hq : Touch q r) (same : level p = level q) :
    band p = band q ∧ rows p = rows q := by
  unfold Touch at hp hq
  unfold level at same
  unfold band rows at same ⊢
  omega

theorem tile_footprint_width_bound (u v x y : Int)
    (same_band : (u + 2 * v) / 32 = (x + 2 * y) / 32)
    (same_rows : v / 16 = y / 16) : -61 ≤ u - x ∧ u - x ≤ 61 := by
  omega

#print axioms overlapping_order_monotone_tiles
#print axioms distinct_overlapping_tiles_increase_level
#print axioms same_level_overlapping_tasks_share_tile
#print axioms tile_footprint_width_bound

end PressureTiles
