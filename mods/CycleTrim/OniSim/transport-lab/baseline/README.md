# Immutable source baseline

Exact native files from `LIghtJUNction/OniMods@6aa40cfd71ebb33d794373f5a830b87f8eefaf01`.
`sha256.json` fixes every source byte. These are the project's existing Rust source files,
not game assemblies or an external decompilation archive. Root LICENSE and LICENSING.md apply.

Do not format or edit these files to make a new candidate pass. `prepare.py` loads the
historical candidate from `../historical-source` and rejects drift in the archived
shared arithmetic/storage helpers. Current production uses separate native tests;
see [the integration record](../INTEGRATION-20261106.md).
