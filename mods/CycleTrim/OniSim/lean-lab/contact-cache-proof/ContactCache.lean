import Std

namespace ContactCache

-- Raw result words and the six x86 sticky exception bits. Actual libm
-- determinism, bounded-input errno preservation and the Rust guard remain
-- explicit assumptions; this is not a compiled floating-point proof.
structure Evaluation where
  word : BitVec 32
  raised : BitVec 6

structure State where
  control : BitVec 10
  flags : BitVec 6
  errno : Int

-- Eligible bounded arithmetic preserves errno and control bits.
def complete (result : Evaluation) (state : State) : BitVec 32 × State :=
  (result.word, { state with flags := state.flags ||| result.raised })

theorem flags_absorb (current prior : BitVec 6)
    (contained : current &&& prior = prior) : current ||| prior = current := by
  apply BitVec.eq_of_getLsbD_eq
  intro i _
  have h := congrArg (fun bits : BitVec 6 => bits.getLsbD i) contained
  simp only [BitVec.getLsbD_and] at h
  simp only [BitVec.getLsbD_or]
  cases hc : current.getLsbD i <;> cases hp : prior.getLsbD i <;> simp_all

theorem earlier_result_effects_retained (current before raised : BitVec 6)
    (contained : current &&& (before ||| raised) = (before ||| raised)) :
    current ||| raised = current := by
  apply BitVec.eq_of_getLsbD_eq
  intro i _
  have h := congrArg (fun bits : BitVec 6 => bits.getLsbD i) contained
  simp only [BitVec.getLsbD_and, BitVec.getLsbD_or] at h
  simp only [BitVec.getLsbD_or]
  cases hc : current.getLsbD i <;> cases hb : before.getLsbD i <;>
    cases hr : raised.getLsbD i <;> simp_all

theorem replay_without_new_effects (result : Evaluation) (state : State)
    (retained : state.flags ||| result.raised = state.flags) :
    complete result state = (result.word, state) := by
  cases state with
  | mk control flags errno => simp_all [complete]

theorem memoized_replay {Key : Type} (evaluate : Key → BitVec 10 → Evaluation)
    (key : Key) (earlier current : State)
    (same_control : current.control = earlier.control)
    (retained : current.flags &&&
      (earlier.flags ||| (evaluate key earlier.control).raised) =
      (earlier.flags ||| (evaluate key earlier.control).raised)) :
    complete (evaluate key current.control) current =
      ((evaluate key earlier.control).word, current) := by
  rw [same_control]
  apply replay_without_new_effects
  exact earlier_result_effects_retained current.flags earlier.flags
    (evaluate key earlier.control).raised retained

theorem arbitrary_continuation {Key Output : Type}
    (evaluate : Key → BitVec 10 → Evaluation)
    (observe : BitVec 32 × State → Output) (key : Key) (earlier current : State)
    (same_control : current.control = earlier.control)
    (retained : current.flags &&&
      (earlier.flags ||| (evaluate key earlier.control).raised) =
      (earlier.flags ||| (evaluate key earlier.control).raised)) :
    observe (complete (evaluate key current.control) current) =
      observe ((evaluate key earlier.control).word, current) :=
  congrArg observe (memoized_replay evaluate key earlier current same_control retained)

#print axioms flags_absorb
#print axioms earlier_result_effects_retained
#print axioms replay_without_new_effects
#print axioms memoized_replay
#print axioms arbitrary_continuation

end ContactCache
