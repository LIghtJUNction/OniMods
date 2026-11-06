import Std

namespace PreparedStep

-- Check is pure and sees the same borrowed state before the first advance.
-- Unlike removing all repeated substep validation, this requires no invariant
-- asserting that advance always preserves valid state.
variable {State Error : Type}

def single (check : State → Except Error Unit)
    (advance : State → Except Error State) (state : State) : Except Error State := do
  check state
  advance state

def duplicate (check : State → Except Error Unit)
    (advance : State → Except Error State) (state : State) : Except Error State := do
  check state
  check state
  advance state

theorem duplicate_eq_single (check : State → Except Error Unit)
    (advance : State → Except Error State) (state : State) :
    duplicate check advance state = single check advance state := by
  unfold duplicate single
  cases h : check state <;> rfl

-- The original first-inner fixed dt is finite and positive. Its time guard
-- always succeeds. Frame elapsed rejection remains before either first step.
def frameOld (timeValid : Bool) (timeError : Error)
    (check : State → Except Error Unit) (advance : State → Except Error State)
    (state : State) : Except Error State :=
  if timeValid then duplicate check advance state else .error timeError

def framePrepared (timeValid : Bool) (timeError : Error)
    (check : State → Except Error Unit) (advance : State → Except Error State)
    (state : State) : Except Error State :=
  if timeValid then single check advance state else .error timeError

theorem frame_result_equal (timeValid : Bool) (timeError : Error)
    (check : State → Except Error Unit) (advance : State → Except Error State)
    (state : State) :
    frameOld timeValid timeError check advance state =
      framePrepared timeValid timeError check advance state := by
  simp [frameOld, framePrepared, duplicate_eq_single]

-- Every later step still checks its newly mutated state. In particular, a
-- failure introduced by an earlier successful advance is observed identically.
def later (check : State → Except Error Unit) (advance : State → Except Error State) :
    Nat → Except Error State → Except Error State
  | 0, result => result
  | count + 1, result => result.bind (fun state => later check advance count (single check advance state))

theorem later_checks_preserved (count : Nat)
    (check : State → Except Error Unit) (advance : State → Except Error State)
    (state : State) :
    later check advance count (duplicate check advance state) =
      later check advance count (single check advance state) :=
  congrArg (later check advance count) (duplicate_eq_single check advance state)

#print axioms duplicate_eq_single
#print axioms frame_result_equal
#print axioms later_checks_preserved

end PreparedStep
