#!/usr/bin/env python3
"""Verify deconstruction preview and execution share the same eligibility decision."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PREVIEW = ROOT / "mods/OniMcp/Tools/Impl/Orders/OrdersDeconstructionTools.cs"
EXECUTION = ROOT / "mods/OniMcp/Tools/Impl/Orders/OrdersPreciseDeconstructionTools.cs"
POLICY = ROOT / "mods/OniMcp/Tools/Core/DeconstructionEligibilityPolicy.cs"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def main() -> int:
    preview = PREVIEW.read_text(encoding="utf-8")
    execution = EXECUTION.read_text(encoding="utf-8")
    policy = POLICY.read_text(encoding="utf-8")

    require(
        "DeconstructionEligibility eligibility = EvaluateDeconstructionEligibility(go);" in preview,
        "deconstruction dry-run must use the shared game-state eligibility evaluator",
    )
    require(
        '["wouldQueue"] = eligibility.CanQueue' in preview,
        "deconstruction dry-run must report the evaluated queueability instead of a constant",
    )
    require(
        '["reasonCode"] = eligibility.ReasonCode' in preview
        and '["reason"] = eligibility.Error' in preview,
        "rejected deconstruction previews must expose a stable reason code and explanation",
    )
    require(
        '["wouldQueue"] = true' not in preview,
        "deconstruction dry-run must not hard-code a successful preview",
    )

    evaluator = "private static DeconstructionEligibility EvaluateDeconstructionEligibility(GameObject go)"
    require(evaluator in execution, "shared deconstruction game-state evaluator is missing")
    require(
        "DeconstructionEligibilityPolicy.Evaluate(" in execution,
        "game-state evaluator must delegate to the host-testable eligibility policy",
    )

    queue = "private static bool TryQueueObjectDeconstruction(GameObject go, JObject args, out string error)"
    require(queue in execution, "deconstruction execution method is missing")
    queue_body = execution.split(queue, 1)[1]
    require(
        "DeconstructionEligibility eligibility = EvaluateDeconstructionEligibility(go);" in queue_body,
        "deconstruction execution must re-check the shared eligibility immediately before mutation",
    )
    require(
        queue_body.index("EvaluateDeconstructionEligibility(go)")
        < queue_body.index("QueueDeconstruction(userTriggered: true)"),
        "building eligibility must be checked before QueueDeconstruction",
    )
    require(
        queue_body.index("EvaluateDeconstructionEligibility(go)")
        < queue_body.index("GameHashes.MarkForDeconstruct"),
        "utility eligibility must be checked before MarkForDeconstruct",
    )

    for token in (
        '"deconstruction_disabled"',
        '"Target does not allow deconstruction"',
        '"not_deconstructable"',
        '"Target is not deconstructable"',
    ):
        require(token in policy, f"deconstruction eligibility policy lost {token}")
    require(
        "if (!allowDeconstruction && !instantBuildMode)" in policy,
        "protected Deconstructable targets must fail closed outside InstantBuild",
    )
    require(
        "if (isUtilityTarget)" in policy,
        "supported utility deconstruction must remain eligible",
    )

    print("OniMcp deconstruction preview/execution eligibility contract verified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
