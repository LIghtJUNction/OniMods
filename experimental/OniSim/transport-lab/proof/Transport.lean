import Std

/-!
Checked models of three implementation obligations, not a proof of all Rust,
IEEE instructions, the native scheduler, or the proprietary game binary.

* The two SSE-style clamps turn every binary32 quotient class into finite [0,1].
  A zero germ count can thus omit its ratio arithmetic.
* A full-key checked, table-scoped memo returns the exact original value and
  preserves its invariant even on collisions, no-store values, and bypass.
* FIFO admission keeps first occurrences in order. This is deliberately stronger
  than set equality: arbitrary stateful consumption and early stops are allowed.
  The bridge from the mutable Rust BFS to its candidate stream is checked by
  ordered differential tests; queue memory/termination are not proved here.

Ideal integer-unit mass/energy bookkeeping is proved separately. It does not
assert exact conservation for rounded floats, sources, sinks, or clamps.
-/
namespace Transport
namespace Germ

def signBit : Nat := 2147483648
def infinityBits : Nat := 2139095040
def oneBits : Nat := 1065353216

/-- Bits of maxss(minss(q, 1), 0), with unordered/equal selecting operand 2.
NaNs select one; negatives (including negative infinity and negative zero) select positive zero. -/
def clampBits (q : Nat) : Nat :=
  if infinityBits < q % signBit then oneBits
  else if signBit ≤ q then 0
  else min q oneBits

theorem clamp_bounded (q : Nat) : clampBits q ≤ oneBits := by
  unfold clampBits
  split <;> try omega
  split <;> simp [oneBits]
  exact Nat.min_le_right _ _

theorem clamp_finite (q : Nat) : clampBits q < infinityBits := by
  have h := clamp_bounded q
  unfold oneBits infinityBits at *
  omega

theorem clamp_positive_sign (q : Nat) : clampBits q < signBit := by
  have h := clamp_bounded q
  unfold oneBits signBit at *
  omega

/-- IEEE zero times a binary32 value: finite inputs retain the XOR sign;
infinite/NaN inputs produce a NaN. Only the finite branch is used below. -/
def zeroProductBits (q : Nat) : Nat :=
  if infinityBits ≤ q % signBit then 2143289344
  else if signBit ≤ q then signBit else 0

theorem zero_germ_product (q : Nat) : zeroProductBits (clampBits q) = 0 := by
  have hf := clamp_finite q
  have hs := clamp_positive_sign q
  simp [zeroProductBits, Nat.mod_eq_of_lt hs, Nat.not_le.mpr hf, Nat.not_le.mpr hs]

theorem nan_quotient_selects_one (q : Nat) (h : infinityBits < q % signBit) :
    clampBits q = oneBits := by simp [clampBits, h]

theorem positive_zero_preserved : clampBits 0 = 0 := by decide

theorem negative_zero_canonicalized : clampBits signBit = 0 := by decide
end Germ

namespace Memo
structure Key where
  a : Fin 65536
  b : Fin 65536
  ia : Fin 256
  ib : Fin 256
  wall : Bool
  deriving DecidableEq

def pack (k : Key) : Nat :=
  k.a.val + 65536 * k.b.val + 4294967296 * k.ia.val +
    1099511627776 * k.ib.val + 281474976710656 * (if k.wall then 1 else 0)

theorem pack_bounded (k : Key) : pack k < 562949953421312 := by
  have := k.a.isLt; have := k.b.isLt; have := k.ia.isLt; have := k.ib.isLt
  unfold pack
  cases k.wall <;> simp only [Bool.false_eq_true, ↓reduceIte] <;> omega

theorem sentinel_ne (k : Key) : pack k ≠ 18446744073709551615 := by
  have := pack_bounded k
  omega

theorem pack_injective (k l : Key) (h : pack k = pack l) : k = l := by
  have ha := k.a.isLt; have hb := k.b.isLt; have hc := k.ia.isLt; have hd := k.ib.isLt
  have la := l.a.isLt; have lb := l.b.isLt; have lc := l.ia.isLt; have ld := l.ib.isLt
  unfold pack at h
  have h0 : k.a.val = l.a.val := by omega
  have h1 : k.b.val = l.b.val := by omega
  have h2 : k.ia.val = l.ia.val := by omega
  have h3 : k.ib.val = l.ib.val := by omega
  have h4 : k.wall = l.wall := by
    cases hkw : k.wall <;> cases hlw : l.wall <;> simp_all <;> omega
  cases k; cases l
  simp_all [Fin.ext_iff]

abbrev Cache (Value : Type) := Nat → Option (Nat × Value)
def Valid {Value : Type} (f : Nat → Value) (c : Cache Value) : Prop :=
  ∀ i k v, c i = some (k, v) → v = f k

def put {Value : Type} (c : Cache Value) (slot k : Nat) (v : Value) : Cache Value :=
  fun i => if i = slot then some (k, v) else c i

theorem put_valid {Value : Type} (f : Nat → Value) (c : Cache Value)
    (hc : Valid f c) (slot k : Nat) : Valid f (put c slot k (f k)) := by
  intro i key value h
  by_cases hi : i = slot
  · simp [put, hi] at h
    rcases h with ⟨rfl, rfl⟩
    rfl
  · exact hc i key value (by simpa [put, hi] using h)

def miss {Value : Type} (f : Nat → Value) (store : Value → Bool)
    (c : Cache Value) (slot k : Nat) : Value × Cache Value :=
  (f k, if store (f k) then put c slot k (f k) else c)

def read {Value : Type} (f : Nat → Value) (store : Value → Bool) (slot : Nat → Nat)
    (bypass : Bool) (c : Cache Value) (k : Nat) : Value × Cache Value :=
  if bypass then (f k, c)
  else match c (slot k) with
    | none => miss f store c (slot k) k
    | some (key, value) => if key = k then (value, c) else miss f store c (slot k) k

theorem miss_spec {Value : Type} (f : Nat → Value) (store : Value → Bool)
    (c : Cache Value) (hc : Valid f c) (slot k : Nat) :
    (miss f store c slot k).1 = f k ∧ Valid f (miss f store c slot k).2 := by
  constructor
  · rfl
  · unfold miss
    dsimp
    split
    · exact put_valid f c hc slot k
    · exact hc

theorem read_spec {Value : Type} (f : Nat → Value) (store : Value → Bool)
    (slot : Nat → Nat) (bypass : Bool) (c : Cache Value) (hc : Valid f c) (k : Nat) :
    (read f store slot bypass c k).1 = f k ∧
      Valid f (read f store slot bypass c k).2 := by
  unfold read
  split
  · exact ⟨rfl, hc⟩
  · cases he : c (slot k) with
    | none => exact miss_spec f store c hc (slot k) k
    | some entry =>
      rcases entry with ⟨key, value⟩
      dsimp
      split
      · rename_i hk
        subst key
        exact ⟨hc (slot k) k value he, hc⟩
      · exact miss_spec f store c hc (slot k) k

def values {Value : Type} (f : Nat → Value) (store : Value → Bool) (slot : Nat → Nat)
    (bypass : Bool) (c : Cache Value) : List Nat → List Value
  | [] => []
  | k :: ks => (read f store slot bypass c k).1 ::
      values f store slot bypass (read f store slot bypass c k).2 ks

theorem values_exact {Value : Type} (f : Nat → Value) (store : Value → Bool)
    (slot : Nat → Nat) (bypass : Bool) (c : Cache Value) (hc : Valid f c) (ks : List Nat) :
    values f store slot bypass c ks = ks.map f := by
  induction ks generalizing c with
  | nil => rfl
  | cons k ks ih =>
    obtain ⟨hv, hi⟩ := read_spec f store slot bypass c hc k
    simp only [values, List.map_cons, hv]
    rw [ih _ hi]

theorem empty_valid {Value : Type} (f : Nat → Value) : Valid f (fun _ => none) := by
  intro _ _ _ h
  contradiction

theorem ordered_updates_exact {Value State : Type} (f : Nat → Value) (store : Value → Bool)
    (slot : Nat → Nat) (bypass : Bool) (c : Cache Value) (hc : Valid f c)
    (ks : List Nat) (step : State → Value → State) (s : State) :
    (values f store slot bypass c ks).foldl step s = (ks.map f).foldl step s := by
  rw [values_exact f store slot bypass c hc ks]
/-- The branch decision may vary at every lookup, as in the adaptive probe. -/
def adaptiveValues {Value : Type} (f : Nat → Value) (store : Value → Bool)
    (slot : Nat → Nat) (c : Cache Value) : List (Bool × Nat) → List Value
  | [] => []
  | (bypass, k) :: ks => (read f store slot bypass c k).1 ::
      adaptiveValues f store slot (read f store slot bypass c k).2 ks

theorem adaptive_values_exact {Value : Type} (f : Nat → Value) (store : Value → Bool)
    (slot : Nat → Nat) (c : Cache Value) (hc : Valid f c) (ks : List (Bool × Nat)) :
    adaptiveValues f store slot c ks = ks.map (fun item => f item.2) := by
  induction ks generalizing c with
  | nil => rfl
  | cons entry ks ih =>
    rcases entry with ⟨bypass, k⟩
    obtain ⟨hv, hi⟩ := read_spec f store slot bypass c hc k
    simp only [adaptiveValues, List.map_cons, hv]
    rw [ih _ hi]

end Memo

namespace Admission
variable {Node : Type} [DecidableEq Node]

def firsts (seen : List Node) : List Node → List Node
  | [] => []
  | x :: xs => if x ∈ seen then firsts seen xs else x :: firsts (x :: seen) xs

theorem seen_congr (a b : List Node) (h : ∀ x, x ∈ a ↔ x ∈ b) (xs : List Node) :
    firsts a xs = firsts b xs := by
  induction xs generalizing a b with
  | nil => rfl
  | cons x xs ih =>
    by_cases hx : x ∈ a
    · have hb := (h x).mp hx
      simp only [firsts, if_pos hx, if_pos hb]
      exact ih a b h
    · have hb : x ∉ b := fun he => hx ((h x).mpr he)
      simp only [firsts, if_neg hx, if_neg hb]
      congr 1
      apply ih
      intro y
      simp only [List.mem_cons, h y]

theorem mem_firsts (seen xs : List Node) (x : Node) :
    x ∈ firsts seen xs ↔ x ∈ xs ∧ x ∉ seen := by
  induction xs generalizing seen with
  | nil => simp [firsts]
  | cons y ys ih =>
    by_cases hy : y ∈ seen
    · by_cases he : x = y
      · subst x; simp [firsts, hy, ih]
      · simp [firsts, hy, ih, he]
    · by_cases he : x = y
      · subst x; simp [firsts, hy]
      · simp [firsts, hy, ih, he]

theorem nodup_firsts (seen xs : List Node) : (firsts seen xs).Nodup := by
  induction xs generalizing seen with
  | nil => simp [firsts]
  | cons x xs ih =>
    by_cases hx : x ∈ seen
    · simp only [firsts, if_pos hx]; exact ih seen
    · simp only [firsts, if_neg hx]
      apply List.nodup_cons.mpr
      exact ⟨by simp [mem_firsts], ih (x :: seen)⟩

/-- Enqueue only first new candidates; retain the pending FIFO prefix exactly. -/
theorem fifo_admission (seen pending incoming : List Node) :
    firsts seen (pending ++ incoming) =
      firsts seen pending ++ firsts (pending ++ seen) incoming := by
  induction pending generalizing seen with
  | nil => simp [firsts]
  | cons x xs ih =>
    by_cases hx : x ∈ seen
    · simp only [List.cons_append, firsts, if_pos hx, ih]
      congr 1
      apply seen_congr
      intro y
      by_cases he : y = x
      · subst y; simp [hx]
      · simp [he]
    · simp only [List.cons_append, firsts, if_neg hx, ih, List.cons_append]
      congr 2
      apply seen_congr
      intro y
      simp only [List.mem_append, List.mem_cons]
      simp only [or_assoc, or_comm, or_left_comm]

/-- Replay may use a shared accumulator and stop after any successful visit.
No exchange/commutativity assumption is made about mass or temperature sums. -/
def replay {State : Type} (step : State → Node → State) (stop : State → Bool) :
    List Node → State → State
  | [], s => s
  | x :: xs, s => let next := step s x
                  if stop next then next else replay step stop xs next

theorem stateful_prefix_preserved {State : Type}
    (seen pending incoming : List Node) (step : State → Node → State)
    (stop : State → Bool) (s : State) :
    replay step stop (firsts seen (pending ++ incoming)) s =
    replay step stop (firsts seen pending ++ firsts (pending ++ seen) incoming) s := by
  rw [fifo_admission]

def traversable (targetPhase cellPhase : Nat) : Prop :=
  if targetPhase = 3 then cellPhase ≠ 1 ∧ cellPhase ≠ 2 else cellPhase ≠ 3

theorem vacuum_still_traversable (targetPhase : Nat) : traversable targetPhase 0 := by
  unfold traversable
  split <;> simp

theorem unchanged_elsewhere {Value : Type} (state : Node → Value) (p q : Node)
    (value : Value) (h : q ≠ p) :
    (if q = p then value else state q) = state q := by simp [h]
end Admission

namespace Bookkeeping
/-- Ideal closed-pair transfer, not float rounding or sink behavior. -/
theorem mass_conserved (a b q : Int) (_hq : 0 ≤ q) (ha : q ≤ a) :
    max (a - q) 0 + (b + q) = a + b := by omega

theorem source_nonnegative (a q : Int) (ha : q ≤ a) : 0 ≤ a - q := by omega

theorem energy_conserved (a b q : Int) : (a - q) + (b + q) = a + b := by omega
end Bookkeeping
end Transport
