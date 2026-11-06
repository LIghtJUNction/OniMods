import Std

namespace ReciprocalIndex

def estimate (n d base : Nat) : Nat := n * (base / d) / base

def corrected (n d base : Nat) : Nat :=
  let q := estimate n d base
  if d ≤ n - q * d then q + 1 else q

theorem estimate_lower (n d base : Nat) (hb : 0 < base) :
    estimate n d base * d ≤ n := by
  let m := base / d
  let q := estimate n d base
  have hq : q * base ≤ n * m := Nat.div_mul_le_self (n * m) base
  have hm : m * d ≤ base := Nat.div_mul_le_self base d
  have scaled : (q * d) * base ≤ n * base := by
    calc
      (q * d) * base = (q * base) * d := by ac_rfl
      _ ≤ (n * m) * d := Nat.mul_le_mul_right d hq
      _ = n * (m * d) := Nat.mul_assoc n m d
      _ ≤ n * base := Nat.mul_le_mul_left n hm
  exact Nat.le_of_mul_le_mul_right scaled hb

theorem estimate_upper (n d base : Nat) (hb : 0 < base)
    (hd : 0 < d) (hn : n < base) : n < (estimate n d base + 2) * d := by
  let m := base / d
  let q := estimate n d base
  have decompose : base = m * d + base % d := by
    simpa [m, Nat.mul_comm] using (Nat.div_add_mod base d).symm
  have hq : n * m < (q + 1) * base :=
    (Nat.div_lt_iff_lt_mul hb).mp (Nat.lt_succ_self q)
  have residual : n * (base % d) < base * d :=
    Nat.lt_of_le_of_lt (Nat.mul_le_mul_left n (Nat.le_of_lt (Nat.mod_lt base hd)))
      (Nat.mul_lt_mul_of_pos_right hn hd)
  have scaled : n * base < ((q + 2) * d) * base := by
    calc
      n * base = n * (m * d + base % d) := congrArg (n * ·) decompose
      _ = (n * m) * d + n * (base % d) := by simp [Nat.mul_add, Nat.mul_assoc]
      _ < ((q + 1) * base) * d + base * d :=
        Nat.add_lt_add (Nat.mul_lt_mul_of_pos_right hq hd) residual
      _ = ((q + 2) * d) * base := by
        simp only [Nat.add_mul, Nat.one_mul, Nat.mul_assoc]
        rw [Nat.mul_comm d base]
        omega
  exact Nat.lt_of_mul_lt_mul_right scaled

theorem one_correction (n d q : Nat) (lower : q * d ≤ n)
    (upper : n < (q + 2) * d) :
    (if d ≤ n - q * d then q + 1 else q) = n / d := by
  by_cases bump : d ≤ n - q * d
  · have lo : (q + 1) * d ≤ n := by
      simp only [Nat.add_mul, Nat.one_mul]
      omega
    have hi : n < (q + 1 + 1) * d := by simpa [Nat.add_assoc] using upper
    simpa [bump] using (Nat.div_eq_of_lt_le lo hi).symm
  · have hi : n < (q + 1) * d := by
      simp only [Nat.add_mul, Nat.one_mul]
      omega
    simpa [bump] using (Nat.div_eq_of_lt_le lower hi).symm

theorem corrected_eq_div (n d base : Nat) (hb : 0 < base)
    (hd : 0 < d) (hn : n < base) : corrected n d base = n / d := by
  exact one_correction n d (estimate n d base)
    (estimate_lower n d base hb) (estimate_upper n d base hb hd hn)

theorem reciprocal_fits (d base : Nat) (hb : 0 < base) (hd : 1 < d) :
    base / d < base := by
  apply (Nat.div_lt_iff_lt_mul (by omega : 0 < d)).mpr
  simpa using Nat.mul_lt_mul_of_pos_left hd hb

theorem product_fits (n d base : Nat) (hb : 0 < base) (hd : 1 < d)
    (hn : n < base) : n * (base / d) < base * base := by
  exact Nat.lt_of_le_of_lt
    (Nat.mul_le_mul_left n (Nat.le_of_lt (reciprocal_fits d base hb hd)))
    (Nat.mul_lt_mul_of_pos_right hn hb)

theorem uint64_correct (n d : Nat) (hn : n < 2^64) (hd : 2 ≤ d) :
    corrected n d (2^64) = n / d := by
  exact corrected_eq_div n d (2^64) (by decide) (by omega) hn

theorem uint128_product_fits (n d : Nat) (hn : n < 2^64) (hd : 2 ≤ d) :
    n * ((2^64) / d) < 2^128 := by
  have powers : (2^64 : Nat) * (2^64) = 2^128 := by decide
  rw [← powers]
  exact product_fits n d (2^64) (by decide) (by omega) hn

theorem corrected_fits (n d base : Nat) (hb : 0 < base)
    (hd : 0 < d) (hn : n < base) : corrected n d base < base := by
  rw [corrected_eq_div n d base hb hd hn]
  apply (Nat.div_lt_iff_lt_mul hd).mpr
  have big : base ≤ base * d := by
    simpa using Nat.mul_le_mul_left base hd
  exact Nat.lt_of_lt_of_le hn big

#print axioms estimate_lower
#print axioms estimate_upper
#print axioms one_correction
#print axioms corrected_eq_div
#print axioms reciprocal_fits
#print axioms product_fits

#print axioms uint64_correct
#print axioms uint128_product_fits
#print axioms corrected_fits

end ReciprocalIndex
