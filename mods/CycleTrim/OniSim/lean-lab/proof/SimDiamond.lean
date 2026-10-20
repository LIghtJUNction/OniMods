import Std

/-!
# Bounded four-neighbour search replaced by row intervals

This file specifies the unobstructed rectangular coordinate graph used by the
Rust disease-emitter search. It proves the visited-cell and local-update
properties of a finite breadth-first reference model and of direct row
enumeration. It is not a proof of the proprietary game binary, floating-point
physics, Rust compilation, or the Rust queue's memory operations.

Coordinates are mathematical integers. The bridge from Rust u16 coordinates
must establish legal bounds and rejection of coordinate zero. The parameter
`r` is the original exclusive depth bound (`max_depth`), a natural number. For
`r > 0` the geometric diamond radius is `r - 1`; `r = 0` visits no cells.
-/

namespace SimDiamond

structure Cell where
  x : Int
  y : Int
  deriving DecidableEq, Repr

structure Bounds where
  xmax : Int
  ymax : Int
  deriving DecidableEq, Repr

def Legal (b : Bounds) (p : Cell) : Prop :=
  1 ≤ p.x ∧ p.x ≤ b.xmax ∧ 1 ≤ p.y ∧ p.y ≤ b.ymax

instance (b : Bounds) (p : Cell) : Decidable (Legal b p) :=
  inferInstanceAs (Decidable (_ ∧ _ ∧ _ ∧ _))

def Adjacent (p q : Cell) : Prop :=
  (q.x = p.x + 1 ∧ q.y = p.y) ∨
  (q.x = p.x - 1 ∧ q.y = p.y) ∨
  (q.x = p.x ∧ q.y = p.y + 1) ∨
  (q.x = p.x ∧ q.y = p.y - 1)

def manhattan (p q : Cell) : Nat :=
  (q.x - p.x).natAbs + (q.y - p.y).natAbs

/-- A legal path with exactly `n` four-neighbour steps. -/
inductive Walk (b : Bounds) : Cell → Cell → Nat → Prop where
  | refl (p : Cell) (hp : Legal b p) : Walk b p p 0
  | cons {p q t : Cell} {n : Nat}
      (hp : Legal b p) (edge : Adjacent p q) (tail : Walk b q t n) :
      Walk b p t (n + 1)

theorem Walk.start_legal {b : Bounds} {p q : Cell} {n : Nat}
    (h : Walk b p q n) : Legal b p := by
  cases h with
  | refl _ hp => exact hp
  | cons hp _ _ => exact hp

theorem Walk.end_legal {b : Bounds} {p q : Cell} {n : Nat}
    (h : Walk b p q n) : Legal b q := by
  induction h with
  | refl _ hp => exact hp
  | cons _ _ _ ih => exact ih

theorem adjacent_distance (p q t : Cell) (h : Adjacent p q) :
    manhattan p t ≤ manhattan q t + 1 := by
  unfold Adjacent at h
  unfold manhattan
  rcases h with h | h | h | h <;> rcases h with ⟨hx, hy⟩ <;> omega

theorem Walk.distance_le {b : Bounds} {p q : Cell} {n : Nat}
    (h : Walk b p q n) : manhattan p q ≤ n := by
  induction h with
  | refl p _ => simp [manhattan]
  | @cons p q t n _ edge _ ih =>
    have he := adjacent_distance p q t edge
    omega

/-- Unless already at the target, a legal step decreases distance by one.
The rectangle has no obstacles, so moving towards a legal target stays legal. -/
theorem step_towards {b : Bounds} {p t : Cell}
    (hp : Legal b p) (ht : Legal b t) (hne : p ≠ t) :
    ∃ q, Legal b q ∧ Adjacent p q ∧ manhattan q t + 1 = manhattan p t := by
  rcases hp with ⟨hpx, hpxm, hpy, hpym⟩
  rcases ht with ⟨htx, htxm, hty, htym⟩
  by_cases hx : p.x < t.x
  · refine ⟨⟨p.x + 1, p.y⟩, ?_, ?_, ?_⟩
    · unfold Legal; dsimp; omega
    · exact Or.inl ⟨rfl, rfl⟩
    · unfold manhattan; dsimp; omega
  · by_cases hx' : t.x < p.x
    · refine ⟨⟨p.x - 1, p.y⟩, ?_, ?_, ?_⟩
      · unfold Legal; dsimp; omega
      · exact Or.inr (Or.inl ⟨rfl, rfl⟩)
      · unfold manhattan; dsimp; omega
    · have hxeq : p.x = t.x := by omega
      by_cases hy : p.y < t.y
      · refine ⟨⟨p.x, p.y + 1⟩, ?_, ?_, ?_⟩
        · unfold Legal; dsimp; omega
        · exact Or.inr (Or.inr (Or.inl ⟨rfl, rfl⟩))
        · unfold manhattan; dsimp; omega
      · have hy' : t.y < p.y := by
          by_cases hn : t.y < p.y
          · exact hn
          · have hyeq : p.y = t.y := by omega
            apply False.elim
            apply hne
            cases p; cases t
            simp_all
        refine ⟨⟨p.x, p.y - 1⟩, ?_, ?_, ?_⟩
        · unfold Legal; dsimp; omega
        · exact Or.inr (Or.inr (Or.inr ⟨rfl, rfl⟩))
        · unfold manhattan; dsimp; omega

/-- The Manhattan distance is attainable, including near clipped boundaries. -/
theorem shortest_walk {b : Bounds} {p t : Cell}
    (hp : Legal b p) (ht : Legal b t) : Walk b p t (manhattan p t) := by
  generalize hd : manhattan p t = n
  induction n using Nat.strongRecOn generalizing p with
  | ind n ih =>
    by_cases hpt : p = t
    · subst p
      have hn : n = 0 := by simpa [manhattan] using hd.symm
      subst n
      simpa [manhattan] using Walk.refl t ht
    · obtain ⟨q, hq, edge, heq⟩ := step_towards hp ht hpt
      have hlt : manhattan q t < n := by omega
      have tail := ih (manhattan q t) hlt hq rfl
      have result := Walk.cons hp edge tail
      have hn : manhattan q t + 1 = n := by omega
      simpa only [hn] using result

def ReachWithin (b : Bounds) (c : Cell) (r : Nat) (p : Cell) : Prop :=
  ∃ n, n < r ∧ Walk b c p n

/-- Main graph theorem: bounded four-neighbour reachability is exactly a
rectangle-clipped open Manhattan ball. The bound is strict (`distance < r`). -/
theorem reach_iff_diamond {b : Bounds} {c : Cell} (hc : Legal b c)
    (r : Nat) (p : Cell) :
    ReachWithin b c r p ↔ Legal b p ∧ manhattan c p < r := by
  constructor
  · rintro ⟨n, hn, hwalk⟩
    have hd := hwalk.distance_le
    exact ⟨hwalk.end_legal, by omega⟩
  · rintro ⟨hp, hd⟩
    exact ⟨manhattan c p, hd, shortest_walk hc hp⟩

def intRange (lo hi : Int) : List Int :=
  (List.range (hi - lo + 1).toNat).map (fun k : Nat => lo + (k : Int))

theorem mem_intRange (lo hi z : Int) :
    z ∈ intRange lo hi ↔ lo ≤ z ∧ z ≤ hi := by
  simp only [intRange, List.mem_map, List.mem_range]
  constructor
  · rintro ⟨k, hk, rfl⟩
    omega
  · intro hz
    refine ⟨(z - lo).toNat, ?_, ?_⟩ <;> omega

theorem nodup_intRange (lo hi : Int) : (intRange lo hi).Nodup := by
  exact (List.nodup_range (n := (hi - lo + 1).toNat)).map
    (fun k : Nat => lo + (k : Int)) (by
      intro a b hab heq
      change lo + (a : Int) = lo + (b : Int) at heq
      exact hab (by omega))

def yLo (_b : Bounds) (c : Cell) (r : Nat) : Int :=
  max 1 (c.y - (r : Int) + 1)

def yHi (b : Bounds) (c : Cell) (r : Nat) : Int :=
  min b.ymax (c.y + (r : Int) - 1)

def halfWidth (c : Cell) (r : Nat) (y : Int) : Int :=
  (r : Int) - 1 - ((y - c.y).natAbs : Int)

def xLo (_b : Bounds) (c : Cell) (r : Nat) (y : Int) : Int :=
  max 1 (c.x - halfWidth c r y)

def xHi (b : Bounds) (c : Cell) (r : Nat) (y : Int) : Int :=
  min b.xmax (c.x + halfWidth c r y)

def row (b : Bounds) (c : Cell) (r : Nat) (y : Int) : List Cell :=
  (intRange (xLo b c r y) (xHi b c r y)).map (fun x => ⟨x, y⟩)

/-- Executable row-by-row diamond enumerator. No grid-sized visited array or
queue occurs in this definition. `intRange` is empty when its upper bound is
below its lower bound, so the zero depth bound needs no proof precondition. -/
def scan (b : Bounds) (c : Cell) (r : Nat) : List Cell :=
  (intRange (yLo b c r) (yHi b c r)).flatMap (row b c r)

def InRowIntervals (b : Bounds) (c : Cell) (r : Nat) (p : Cell) : Prop :=
  yLo b c r ≤ p.y ∧ p.y ≤ yHi b c r ∧
  xLo b c r p.y ≤ p.x ∧ p.x ≤ xHi b c r p.y

theorem row_intervals_iff_diamond (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    InRowIntervals b c r p ↔ Legal b p ∧ manhattan c p < r := by
  unfold InRowIntervals yLo yHi xLo xHi halfWidth Legal manhattan
  omega

theorem mem_scan_iff_intervals (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    p ∈ scan b c r ↔ InRowIntervals b c r p := by
  simp only [scan, List.mem_flatMap, row, List.mem_map, mem_intRange]
  constructor
  · rintro ⟨y, hy, x, hx, heq⟩
    subst p
    exact ⟨hy.1, hy.2, hx.1, hx.2⟩
  · intro h
    exact ⟨p.y, ⟨h.1, h.2.1⟩, p.x, ⟨h.2.2.1, h.2.2.2⟩, by cases p; rfl⟩

theorem mem_scan_iff_diamond (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    p ∈ scan b c r ↔ Legal b p ∧ manhattan c p < r :=
  (mem_scan_iff_intervals b c r p).trans (row_intervals_iff_diamond b c r p)

theorem mem_scan_iff_reach {b : Bounds} {c : Cell} (hc : Legal b c)
    (r : Nat) (p : Cell) : p ∈ scan b c r ↔ ReachWithin b c r p :=
  (mem_scan_iff_diamond b c r p).trans (reach_iff_diamond hc r p).symm

theorem nodup_row (b : Bounds) (c : Cell) (r : Nat) (y : Int) :
    (row b c r y).Nodup := by
  exact (nodup_intRange (xLo b c r y) (xHi b c r y)).map
    (fun x : Int => (⟨x, y⟩ : Cell)) (by
      intro a b hab heq
      exact hab (congrArg Cell.x heq))

theorem row_member_y {b : Bounds} {c : Cell} {r : Nat} {y : Int} {p : Cell}
    (h : p ∈ row b c r y) : p.y = y := by
  obtain ⟨x, _, rfl⟩ := List.mem_map.mp h
  rfl

/-- No cell occurs twice, even on clipped rows and at corner boundaries. -/
theorem nodup_scan (b : Bounds) (c : Cell) (r : Nat) : (scan b c r).Nodup := by
  unfold scan
  apply List.pairwise_flatMap.mpr
  constructor
  · intro y _
    exact nodup_row b c r y
  · apply List.Pairwise.imp _ (nodup_intRange (yLo b c r) (yHi b c r))
    intro a d had p hp q hq heq
    apply had
    have hpy := row_member_y hp
    have hqy := row_member_y hq
    have := congrArg Cell.y heq
    omega

theorem scan_zero (b : Bounds) (c : Cell) : scan b c 0 = [] := by
  apply List.eq_nil_iff_forall_not_mem.mpr
  intro p hp
  have hd := (mem_scan_iff_diamond b c 0 p).mp hp
  omega

/-- The Rust-facing enumeration rejects an illegal converted center. -/
def checkedScan (b : Bounds) (c : Cell) (r : Nat) : List Cell :=
  if Legal b c then scan b c r else []

theorem checkedScan_illegal {b : Bounds} {c : Cell} (h : ¬ Legal b c) (r : Nat) :
    checkedScan b c r = [] := by
  simp [checkedScan, h]

theorem checkedScan_zero (b : Bounds) (c : Cell) : checkedScan b c 0 = [] := by
  simp [checkedScan, scan_zero]

theorem mem_checkedScan_iff_reach (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    p ∈ checkedScan b c r ↔ ReachWithin b c r p := by
  by_cases hc : Legal b c
  · simpa [checkedScan, hc] using mem_scan_iff_reach hc r p
  · simp only [checkedScan, if_neg hc, List.not_mem_nil, false_iff]
    rintro ⟨_, _, hw⟩
    exact hc hw.start_legal

theorem mem_checkedScan_iff_diamond (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    p ∈ checkedScan b c r ↔
      Legal b c ∧ Legal b p ∧ manhattan c p < r := by
  by_cases hc : Legal b c <;> simp [checkedScan, hc, mem_scan_iff_diamond]

theorem nodup_checkedScan (b : Bounds) (c : Cell) (r : Nat) :
    (checkedScan b c r).Nodup := by
  by_cases hc : Legal b c
  · simpa [checkedScan, hc] using nodup_scan b c r
  · simp [checkedScan, hc]

/-- Legal successors in the original four-direction order. -/
def neighbors (b : Bounds) (p : Cell) : List Cell :=
  [⟨p.x, p.y - 1⟩, ⟨p.x - 1, p.y⟩,
   ⟨p.x + 1, p.y⟩, ⟨p.x, p.y + 1⟩].filter (fun q => decide (Legal b q))

theorem mem_neighbors_iff (b : Bounds) (p q : Cell) :
    q ∈ neighbors b p ↔ Adjacent p q ∧ Legal b q := by
  cases p
  cases q
  simp [neighbors, Adjacent, or_comm, or_left_comm, or_assoc]

/-- Finite, unpruned breadth layer: all legal endpoints after exactly `n`
steps. Repeated endpoints are deliberately retained until `unique` below.
This executable reference specification is used for proofs, not performance.
It is not a line-by-line encoding of the original Rust queue implementation. -/
def frontier (b : Bounds) (c : Cell) : Nat → List Cell
  | 0 => if Legal b c then [c] else []
  | n + 1 => if Legal b c then
      (neighbors b c).flatMap (fun q => frontier b q n) else []

theorem mem_frontier_iff_walk (b : Bounds) (c p : Cell) (n : Nat) :
    p ∈ frontier b c n ↔ Walk b c p n := by
  induction n generalizing c with
  | zero =>
    by_cases hc : Legal b c
    · simp only [frontier, if_pos hc, List.mem_singleton]
      constructor
      · intro h; subst p; exact Walk.refl c hc
      · intro h; cases h; rfl
    · simp only [frontier, if_neg hc, List.not_mem_nil, false_iff]
      intro h
      exact hc h.start_legal
  | succ n ih =>
    by_cases hc : Legal b c
    · simp only [frontier, if_pos hc, List.mem_flatMap, mem_neighbors_iff, ih]
      constructor
      · rintro ⟨q, ⟨edge, _⟩, hw⟩
        exact Walk.cons hc edge hw
      · intro hw
        cases hw with
        | cons _ edge tail => exact ⟨_, ⟨edge, tail.start_legal⟩, tail⟩
    · simp only [frontier, if_neg hc, List.not_mem_nil, false_iff]
      intro h
      exact hc h.start_legal

/-- Stable first-occurrence deduplication, implemented here so its coverage
and uniqueness properties are proved locally without a BFS-coverage premise. -/
def unique : List Cell → List Cell
  | [] => []
  | p :: ps => p :: (unique ps).filter (fun q => decide (q ≠ p))

theorem mem_unique_iff (xs : List Cell) (p : Cell) : p ∈ unique xs ↔ p ∈ xs := by
  induction xs with
  | nil => simp [unique]
  | cons q xs ih =>
    by_cases h : p = q <;> simp [unique, ih, h]

theorem nodup_unique (xs : List Cell) : (unique xs).Nodup := by
  induction xs with
  | nil => simp [unique]
  | cons p ps ih =>
    apply List.nodup_cons.mpr
    constructor
    · simp
    · exact ih.filter _

/-- Breadth-first reference visit list: concatenate depths strictly below r,
then keep the first visit of every cell. -/
def bfs (b : Bounds) (c : Cell) (r : Nat) : List Cell :=
  unique ((List.range r).flatMap (fun n => frontier b c n))

theorem mem_bfs_iff_reach (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    p ∈ bfs b c r ↔ ReachWithin b c r p := by
  simp only [bfs, mem_unique_iff, List.mem_flatMap, List.mem_range,
    mem_frontier_iff_walk, ReachWithin]

theorem nodup_bfs (b : Bounds) (c : Cell) (r : Nat) : (bfs b c r).Nodup :=
  nodup_unique _

/-- Unconditional visit-set equivalence, including illegal centers and r=0. -/
theorem bfs_checkedScan_same_cells (b : Bounds) (c : Cell) (r : Nat) (p : Cell) :
    p ∈ bfs b c r ↔ p ∈ checkedScan b c r :=
  (mem_bfs_iff_reach b c r p).trans (mem_checkedScan_iff_reach b c r p).symm

/-- A grid state can contain arbitrary per-cell values, including a tuple of
all disease fields. The local function may depend on coordinates and on
read-only emitter/table data captured in its closure. It cannot read or write
another cell or a shared mutable accumulator. -/
def writeOne {Value : Type} (update : Cell → Value → Value)
    (state : Cell → Value) (p : Cell) : Cell → Value :=
  fun q => if q = p then update q (state q) else state q

def runUpdates {Value : Type} (update : Cell → Value → Value) :
    List Cell → (Cell → Value) → (Cell → Value)
  | [], state => state
  | p :: ps, state => runUpdates update ps (writeOne update state p)

/-- One update at each member, no update at any non-member. -/
theorem runUpdates_at {Value : Type} (update : Cell → Value → Value)
    (xs : List Cell) (hx : xs.Nodup) (state : Cell → Value) (q : Cell) :
    runUpdates update xs state q =
      if q ∈ xs then update q (state q) else state q := by
  induction xs generalizing state with
  | nil => simp [runUpdates]
  | cons p ps ih =>
    obtain ⟨hp, hps⟩ := List.nodup_cons.mp hx
    simp only [runUpdates]
    rw [ih hps]
    by_cases hqp : q = p
    · subst q
      simp [hp, writeOne]
    · by_cases hqps : q ∈ ps <;> simp [hqp, hqps, writeOne]

/-- Order independence under the explicit cell-local update contract. -/
theorem runUpdates_same_set {Value : Type} (update : Cell → Value → Value)
    (xs ys : List Cell) (hx : xs.Nodup) (hy : ys.Nodup)
    (same : ∀ p, p ∈ xs ↔ p ∈ ys) (state : Cell → Value) :
    runUpdates update xs state = runUpdates update ys state := by
  funext q
  rw [runUpdates_at update xs hx, runUpdates_at update ys hy]
  simp only [same q]

/-- Main semantic equivalence theorem. No BFS-coverage or scan-equality
assumption is supplied: those facts were established above. The abstract
local-update contract is the only restriction on update behavior. -/
theorem bfs_checkedScan_same_updates {Value : Type}
    (b : Bounds) (c : Cell) (r : Nat) (update : Cell → Value → Value)
    (state : Cell → Value) :
    runUpdates update (bfs b c r) state =
      runUpdates update (checkedScan b c r) state := by
  exact runUpdates_same_set update _ _ (nodup_bfs b c r)
    (nodup_checkedScan b c r) (bfs_checkedScan_same_cells b c r) state

def linearIndex (stride : Int) (p : Cell) : Int := p.y * stride + p.x

/-- Distinct valid columns cannot alias in the row-major index calculation.
This is mathematical integer arithmetic; usize overflow remains an explicit
implementation-side array-size precondition. -/
theorem linearIndex_injective (stride : Int) (p q : Cell)
    (hpx : 0 ≤ p.x) (hpxw : p.x < stride)
    (hqx : 0 ≤ q.x) (hqxw : q.x < stride)
    (heq : linearIndex stride p = linearIndex stride q) : p = q := by
  have hw : stride ≠ 0 := by omega
  have hmod := congrArg (fun n : Int => n % stride) heq
  have hx : p.x = q.x := by
    simpa only [linearIndex, Int.add_comm (p.y * stride),
      Int.add_comm (q.y * stride), Int.add_mul_emod_self,
      Int.emod_eq_of_lt hpx hpxw, Int.emod_eq_of_lt hqx hqxw] using hmod
  have hmul : p.y * stride = q.y * stride := by
    unfold linearIndex at heq
    omega
  have hy := Int.eq_of_mul_eq_mul_right hw hmul
  cases p; cases q
  simp_all

/-- Effective inclusive bounds after u16 coordinate conversion. `65536` is
the first excluded upper coordinate, and zero is always excluded. -/
def effectiveBounds (stride height : Nat) : Bounds :=
  ⟨(min stride 65536 : Nat) - (1 : Int),
   (min height 65536 : Nat) - (1 : Int)⟩

theorem legal_effectiveBounds (stride height : Nat) (p : Cell) :
    Legal (effectiveBounds stride height) p ↔
      0 < p.x ∧ p.x < (min stride 65536 : Nat) ∧
      0 < p.y ∧ p.y < (min height 65536 : Nat) := by
  unfold Legal effectiveBounds
  dsimp
  omega

end SimDiamond
