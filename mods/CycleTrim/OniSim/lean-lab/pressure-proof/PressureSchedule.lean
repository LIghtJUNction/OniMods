import Std

/-!
Footprints and cell-level scheduling only. Coordinates use scan-oriented x:
the pressure side neighbour is x+1 even when the physical scan is reversed.
One task may read/write its two-by-two footprint. Immutable old snapshots and
tables may be captured by a local transition. This does not prove that the
current Rust kernel meets that footprint contract, a tile planner, event order,
parallel memory safety, or compiled simulation equivalence.
-/
namespace PressureSchedule

structure Cell where
  x : Int
  y : Int
  deriving DecidableEq

def Touch (p r : Cell) : Prop :=
  p.x ≤ r.x ∧ r.x ≤ p.x + 1 ∧ p.y ≤ r.y ∧ r.y ≤ p.y + 1

instance (p r : Cell) : Decidable (Touch p r) :=
  inferInstanceAs (Decidable (_ ∧ _ ∧ _ ∧ _))

def Before (p q : Cell) : Prop := p.y < q.y ∨ (p.y = q.y ∧ p.x < q.x)
def wave (p : Cell) : Int := p.x + 2 * p.y

theorem ordered_overlap_increases_wave (p q r : Cell)
    (hp : Touch p r) (hq : Touch q r) (order : Before p q) : wave p < wave q := by
  unfold Touch at hp hq
  unfold Before at order
  unfold wave
  omega

theorem same_wave_overlap_equal (p q r : Cell)
    (hp : Touch p r) (hq : Touch q r) (same : wave p = wave q) : p = q := by
  unfold Touch at hp hq
  unfold wave at same
  have hx : p.x = q.x := by omega
  have hy : p.y = q.y := by omega
  cases p
  cases q
  simp_all

def Disjoint (p q : Cell) : Prop := ∀ r, ¬ (Touch p r ∧ Touch q r)

theorem distinct_same_wave_disjoint (p q : Cell) (different : p ≠ q)
    (same : wave p = wave q) : Disjoint p q := by
  intro r both
  exact different (same_wave_overlap_equal p q r both.1 both.2 same)

def view {Value : Type} (p : Cell) (state : Cell → Value) : Cell → Option Value :=
  fun r => if Touch p r then some (state r) else none

def applyTask {Value : Type} (p : Cell)
    (transition : (Cell → Option Value) → Cell → Value) (state : Cell → Value) : Cell → Value :=
  fun r => if Touch p r then transition (view p state) r else state r

theorem view_after_disjoint {Value : Type} (p q : Cell) (separate : Disjoint p q)
    (transition : (Cell → Option Value) → Cell → Value) (state : Cell → Value) :
    view p (applyTask q transition state) = view p state := by
  funext r
  by_cases hp : Touch p r
  · have hq : ¬ Touch q r := fun h => separate r ⟨hp, h⟩
    simp [view, applyTask, hp, hq]
  · simp [view, hp]

theorem disjoint_tasks_commute {Value : Type} (p q : Cell) (separate : Disjoint p q)
    (left right : (Cell → Option Value) → Cell → Value) (state : Cell → Value) :
    applyTask p left (applyTask q right state) = applyTask q right (applyTask p left state) := by
  have reverse : Disjoint q p := fun r both => separate r ⟨both.2, both.1⟩
  have vp := view_after_disjoint p q separate right state
  have vq := view_after_disjoint q p reverse left state
  funext r
  by_cases hp : Touch p r
  · have hq : ¬ Touch q r := fun h => separate r ⟨hp, h⟩
    simp [applyTask, hp, hq, vp]
  · by_cases hq : Touch q r
    · simp [applyTask, hp, hq, vq]
    · simp [applyTask, hp, hq]

theorem earlier_inversion_disjoint (p q : Cell) (order : Before p q)
    (inverted : wave q < wave p) : Disjoint p q := by
  intro r both
  have increasing := ordered_overlap_increases_wave p q r both.1 both.2 order
  omega

def insertByWave (p : Cell) : List Cell → List Cell
  | [] => [p]
  | q :: rest => if wave p ≤ wave q then p :: q :: rest else q :: insertByWave p rest

def sortByWave : List Cell → List Cell
  | [] => []
  | p :: rest => insertByWave p (sortByWave rest)

theorem insert_membership (p key : Cell) (tasks : List Cell) :
    key ∈ insertByWave p tasks ↔ key = p ∨ key ∈ tasks := by
  induction tasks with
  | nil => simp [insertByWave]
  | cons q rest ih =>
    by_cases ordered : wave p ≤ wave q
    · simp [insertByWave, ordered]
    · simp [insertByWave, ordered, ih, or_assoc, or_left_comm, or_comm]

theorem sort_membership (key : Cell) (tasks : List Cell) :
    key ∈ sortByWave tasks ↔ key ∈ tasks := by
  induction tasks with
  | nil => simp [sortByWave]
  | cons p rest ih => simp [sortByWave, insert_membership, ih]

def execute {Value : Type}
    (transition : Cell → (Cell → Option Value) → Cell → Value) :
    List Cell → (Cell → Value) → (Cell → Value)
  | [], state => state
  | p :: rest, state => execute transition rest (applyTask p (transition p) state)

theorem insertion_preserves_execution {Value : Type}
    (transition : Cell → (Cell → Option Value) → Cell → Value)
    (p : Cell) (tasks : List Cell) (state : Cell → Value)
    (before : ∀ q ∈ tasks, Before p q) :
    execute transition (insertByWave p tasks) state =
      execute transition tasks (applyTask p (transition p) state) := by
  induction tasks generalizing state with
  | nil => rfl
  | cons q rest ih =>
    by_cases ordered : wave p ≤ wave q
    · simp [insertByWave, ordered, execute]
    · have separate := earlier_inversion_disjoint p q (before q (by simp)) (by omega)
      have remaining : ∀ r ∈ rest, Before p r := fun r member => before r (by simp [member])
      simp only [insertByWave, ordered, if_false, execute]
      rw [ih _ remaining]
      rw [disjoint_tasks_commute p q separate (transition p) (transition q) state]

theorem wave_sort_preserves_execution {Value : Type}
    (transition : Cell → (Cell → Option Value) → Cell → Value)
    (tasks : List Cell) (state : Cell → Value) (ordered : tasks.Pairwise Before) :
    execute transition (sortByWave tasks) state = execute transition tasks state := by
  induction tasks generalizing state with
  | nil => rfl
  | cons p rest ih =>
    have pair := List.pairwise_cons.mp ordered
    have before : ∀ q ∈ sortByWave rest, Before p q := by
      intro q member
      exact pair.1 q ((sort_membership q rest).mp member)
    simp only [sortByWave]
    rw [insertion_preserves_execution transition p (sortByWave rest) state before]
    exact ih _ pair.2

#print axioms ordered_overlap_increases_wave
#print axioms same_wave_overlap_equal
#print axioms distinct_same_wave_disjoint
#print axioms view_after_disjoint
#print axioms disjoint_tasks_commute
#print axioms earlier_inversion_disjoint
#print axioms insert_membership
#print axioms sort_membership
#print axioms insertion_preserves_execution
#print axioms wave_sort_preserves_execution

end PressureSchedule
