import Std

/-!
An abstract seen-set suppresses only records known to remain in the unpublished
event list. This proves membership preservation, not the Rust bit indexing,
allocation/aliasing, validation errors, or the final sorting implementation.
-/
namespace NotificationMembership

structure Pending where
  events : List Int
  seen : Int → Bool

def Sound (s : Pending) : Prop := ∀ cell, s.seen cell = true → cell ∈ s.events

def clear : Pending := ⟨[], fun _ => false⟩

def record (s : Pending) (cell : Int) (tracked : Bool) : Pending :=
  if tracked then
    if s.seen cell then s
    else ⟨s.events ++ [cell], fun index => if index = cell then true else s.seen index⟩
  else { s with events := s.events ++ [cell] }

theorem clear_sound : Sound clear := by
  intro cell marked
  simp [clear] at marked

theorem record_membership (s : Pending) (sound : Sound s) (cell key : Int) (tracked : Bool) :
    key ∈ (record s cell tracked).events ↔ key = cell ∨ key ∈ s.events := by
  cases tracked with
  | false => simp [record, or_comm]
  | true =>
    cases marked : s.seen cell with
    | false => simp [record, marked, or_comm]
    | true =>
      have present := sound cell marked
      have equivalent : key ∈ s.events ↔ key = cell ∨ key ∈ s.events := by
        constructor
        · exact fun member => Or.inr member
        · intro alternatives
          rcases alternatives with equal | member
          · simpa [equal] using present
          · exact member
      simpa [record, marked] using equivalent

theorem record_sound (s : Pending) (sound : Sound s) (cell : Int) (tracked : Bool) :
    Sound (record s cell tracked) := by
  intro key marked
  cases tracked with
  | false =>
    have previous : s.seen key = true := by simpa [record] using marked
    exact (record_membership s sound cell key false).mpr (Or.inr (sound key previous))
  | true =>
    cases previous_cell : s.seen cell with
    | true =>
      have previous : s.seen key = true := by simpa [record, previous_cell] using marked
      exact (record_membership s sound cell key true).mpr (Or.inr (sound key previous))
    | false =>
      by_cases equal : key = cell
      · exact (record_membership s sound cell key true).mpr (Or.inl equal)
      · have previous : s.seen key = true := by simpa [record, previous_cell, equal] using marked
        exact (record_membership s sound cell key true).mpr (Or.inr (sound key previous))

theorem raw_append_sound (s : Pending) (sound : Sound s) (more : List Int) :
    Sound { s with events := s.events ++ more } := by
  intro cell marked
  exact List.mem_append.mpr (Or.inl (sound cell marked))

theorem fold_membership (track : Int → Bool) (input : List Int) (s : Pending)
    (sound : Sound s) (key : Int) :
    key ∈ (input.foldl (fun pending cell => record pending cell (track cell)) s).events ↔
    key ∈ s.events ∨ key ∈ input := by
  induction input generalizing s with
  | nil => simp
  | cons cell rest ih =>
    rw [List.foldl_cons, ih (record s cell (track cell)) (record_sound s sound cell (track cell))]
    rw [record_membership s sound cell key (track cell)]
    simp only [List.mem_cons]
    simp only [or_assoc, or_left_comm, or_comm]

#print axioms clear_sound
#print axioms record_membership
#print axioms record_sound
#print axioms raw_append_sound
#print axioms fold_membership

end NotificationMembership
