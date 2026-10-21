import Std

/-!
The finite coordinate planner for scan-oriented pressure cells. The Rust
proposal visits wave t, then rows from ceil(saturating_sub(t,width-1)/2)
through min(t/2,height-1), emitting x=t-2*y. This proves the arithmetic
enumeration, not fold equivalence, machine overflow or parallel execution.
-/
namespace PressureEnumeration

def InWindow (width height t y : Nat) : Prop :=
  (t - (width - 1) + 1) / 2 ≤ y ∧ y ≤ min (t / 2) (height - 1)

theorem window_exact (width height t y : Nat) (hw : 0 < width) (hh : 0 < height) :
    InWindow width height t y ↔ y < height ∧ 2 * y ≤ t ∧ t - 2 * y < width := by
  unfold InWindow
  omega

theorem every_cell_is_in_window (width height x y : Nat)
    (hx : x < width) (hy : y < height) :
    InWindow width height (x + 2 * y) y := by
  unfold InWindow
  omega

theorem every_cell_wave_is_visited (width height x y : Nat)
    (hx : x < width) (hy : y < height) :
    x + 2 * y < width + 2 * (height - 1) := by
  omega

theorem emitted_coordinate_reconstructs_unique_wave (width height t y : Nat)
    (hw : 0 < width) (hh : 0 < height) (member : InWindow width height t y) :
    (t - 2 * y) + 2 * y = t ∧
      ∀ other, other = (t - 2 * y) + 2 * y → other = t := by
  have bounds := (window_exact width height t y hw hh).mp member
  have reconstruct := Nat.sub_add_cancel bounds.2.1
  constructor
  · exact reconstruct
  · intro other equal
    exact equal.trans reconstruct

#print axioms window_exact
#print axioms every_cell_is_in_window
#print axioms every_cell_wave_is_visited
#print axioms emitted_coordinate_reconstructs_unique_wave

end PressureEnumeration
