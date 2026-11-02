import Std

/-!
Pure model of the proposed same-species disease mutation. Counts are bitvectors
with signed comparison, and growth is an opaque 32-bit payload. The different
species calculation is abstract: the theorem does not prove its floating-point
formula, the proprietary library, Rust compilation, aliasing or bounds checks.
-/
namespace DiseaseMerge

structure State where
  id : BitVec 8
  count : BitVec 32
  infestation : BitVec 8
  growthBits : BitVec 32
  deriving DecidableEq

def clear : State := ⟨255, 0, 0, 0⟩

abbrev Resolver := State → BitVec 8 → BitVec 32 → BitVec 8 × BitVec 32

def general (resolve : Resolver) (s : State) (incoming : BitVec 8) (delta : BitVec 32) : State :=
  let (id, n) := if s.id = incoming then (s.id, s.count + delta) else resolve s incoming delta
  let changed := { s with id := id, count := n }
  if n.toInt ≤ 0 then clear
  else if id = s.id then changed else { changed with infestation := 0 }

def optimized (resolve : Resolver) (s : State) (incoming : BitVec 8) (delta : BitVec 32) : State :=
  if s.id = incoming then
    let n := s.count + delta
    if n.toInt ≤ 0 then clear else { s with count := n }
  else general resolve s incoming delta

theorem optimized_eq_general (resolve : Resolver) (s : State)
    (incoming : BitVec 8) (delta : BitVec 32) :
    optimized resolve s incoming delta = general resolve s incoming delta := by
  by_cases h : s.id = incoming
  · simp [optimized, general, h]
  · simp [optimized, h]

theorem same_species_nonpositive_clears (resolve : Resolver) (s : State) (delta : BitVec 32)
    (h : (s.count + delta).toInt ≤ 0) :
    optimized resolve s s.id delta = clear := by
  unfold optimized
  rw [if_pos (Eq.refl s.id)]
  exact if_pos h

theorem same_species_positive_preserves_metadata (resolve : Resolver) (s : State) (delta : BitVec 32)
    (h : 0 < (s.count + delta).toInt) :
    (optimized resolve s s.id delta).id = s.id ∧
    (optimized resolve s s.id delta).infestation = s.infestation ∧
    (optimized resolve s s.id delta).growthBits = s.growthBits := by
  have nonpositive_false : ¬ (s.count + delta).toInt ≤ 0 := by omega
  simp only [optimized, if_pos rfl, if_neg nonpositive_false]
  exact ⟨rfl, rfl, rfl⟩

#print axioms optimized_eq_general
#print axioms same_species_nonpositive_clears
#print axioms same_species_positive_preserves_metadata

end DiseaseMerge
