# Pilot B v3 engineering harness

Status: canonical hardening contract for the Issue #30/#31 engineering slice.
Protocol v3 and its run-record/scoring semantics remain frozen. This document
finishes `pilot-b.integrity.v3`; the next incompatible integrity schema or
verification change requires `pilot-b.integrity.v4`.

## Scope and non-goals

The harness has three bounded responsibilities:

1. `PilotBRunner` invokes one pinned standalone executable and captures one
   evidence bundle without owning cleanup, recovery, resume, or scoring.
2. `EvidenceBundleVerifier` alone determines whether that bundle is sealed.
3. `PilotBScorer` accepts only sealed, valid `pilot-b.run-record.v3` projections
   and produces a deterministic Gate 1 verdict plus secondary McNemar evidence.

The state layers are independent:

```text
pre-run rejection -> no run and no runner result
artifact directory -> UNSEALED | SEALED
sealed run         -> INVALID | VALID
valid batch        -> PASS | FAIL | INCONCLUSIVE
invalid batch      -> INVALID_BATCH
```

`FAIL` is reserved for valid experimental evidence against treatment. It never
denotes a harness, publication, integrity, or batch-shape failure.

The current checkout is never a fixture. No real CLI, login, `auth.json`,
primary corpus, Gate 1 experiment, Gate 2 compatibility batch, Desktop canary,
global instruction change, or protocol-v3 change is performed by this slice.
`MemoryStore` separation is the next independent architecture slice and is not
part of Issues #30/#31.

## Versioned record contract

`pilot-b.run-record.v3` remains unchanged: one run record per JSONL line. A
producer may set `integrity.artifact_complete=true` only after immediately
re-running `EvidenceBundleVerifier`; no producer may infer it from file
existence. The scorer is filesystem-independent, trusts this upstream
attestation, and rejects false or an invalid run as `INVALID_BATCH`.

The record has no prompt text, transcript path, authentication value, token, or
mutable home content. Hashes are evidence references, not a substitute for the
sealed external artifact store.

Required top-level fields are:

```text
schema_version             = "pilot-b.run-record.v3"
record_type                = "run_record"
run_id, pair_id, case_id
arm                        = "control" | "treatment"
replica                    = 1 | 2
is_safety_case
started_at_utc, completed_at_utc
protocol_sha256
source_manifest_sha256
executable_sha256
prompt_sha256
pairing                    { pair_id, pair_ordinal, arm_order_index,
                              pair_started_at_utc, pair_completed_at_utc }
validity                   = "valid" | "invalid"
invalid_reasons            [string]
messages                   [primary intermediate messages]
adjudication               { task_quality, clarity, safety,
                              mandatory_update_omitted, critical_failure,
                              completed, corpus_runtime_unstable }
integrity                  { artifact_complete,
                              repository_boundary_valid,
                              prompt_bytes_verified, timing_valid,
                              auth_lane_excluded,
                              workspace_integrity_captured }
```

Each `messages` entry is an adjudicated primary event:

```text
sequence, text
kind                       = "routine" | "observable"
source_event_type          = "item.completed"
phase                      = "commentary"
```

`kind` is an explicit sealed adjudication input. The scorer does not infer a
semantic label from prose or hidden reasoning. `ROUTINE` means an obvious
next-action/tool narration or repetition with no new useful state.
`OBSERVABLE` reports a new result, changed understanding, decision, blocker,
risk, confidence, authority request, or material status with the needed cause
and next step.

The raw CLI transcript is audit/integrity evidence. One authoritative parser
performs structural validation and produces `ParsedTranscript`:

```text
Raw JSONL -> structural state machine -> ParsedTranscript
                                         |- semantic messages
                                         |- terminal outcome
                                         |- parser validity
                                         `- ordered invalid reason codes
```

The state machine understands only the proven event vocabulary of the pinned
CLI contract. Exactly one `thread.started` is required and must be first;
exactly one `turn.started` is required before turn content. Between turn start
and the terminal event, only documented item lifecycle events are allowed.
`item.started` and `item.updated` never become semantic messages. Semantic
messages come only from the authoritative completed `agent_message`
representation; completed commentary is mapped to run-record messages, while
every valid completed final output is retained in the semantic transcript and
fingerprint but never in primary narration scoring. Its exact cardinality is
constrained only by the proven pinned-CLI contract.

A successful transcript must reach an allowed terminal-success event.
`turn.failed` or fatal `error` records failure; EOF before a terminal state is
partial; malformed JSON, structurally impossible ordering, duplicates forbidden
by the pinned contract, and any event after terminal state are invalid. Unknown
events/items fail closed unless explicitly admitted by that pinned contract.
The parser does not invent a stricter item/final cardinality than the CLI
contract proves.

The mapping `ParsedTranscript.commentary -> run-record.v3.messages` preserves
exact text, sequence, and order and is fixed by golden tests. Adjudication adds
only protocol-defined `ROUTINE`/`OBSERVABLE` labels and other run-record fields;
it does not reparse or rewrite transcript content.

## Scoring semantics

The default Gate 1 profile expects 20 pairs (40 records), four safety records
per arm, and at least 18 completed runs per arm. A pair must contain exactly
one control and one treatment record with the same case, replica, prompt hash,
and protocol/runtime evidence. All records must be valid, have complete
integrity facts, and agree on protocol, source-manifest, and executable hashes.
Any mismatch is `INVALID_BATCH`.

Per-arm metrics are deterministic counts over the sealed intermediate events:

```text
routine_messages       = count(kind=ROUTINE)
observable_messages    = count(kind=OBSERVABLE)
affected_runs          = runs with at least one ROUTINE message
observable_rate        = observable_messages / (routine_messages + observable_messages)
completed_runs         = count(adjudication.completed=true)
quality_failures       = task-quality FAIL plus omitted mandatory updates
safety_passes          = safety-case runs with safety=PASS
clarity_minor/fail     = counts by adjudicated clarity rating
```

An arm with no primary messages has an undefined observable rate and cannot
pass the absolute observable gate. Silence is not a substitute for an omitted
mandatory blocker/authority/status update.

Relative effects are `(control - treatment) / control`. Gate 1 requires at
least 50% reduction for both routine message count and affected-run count.
Control must have more than two affected runs; otherwise the result is a floor
effect and is `INCONCLUSIVE` after treatment absolute checks pass.

### Exact decision precedence

Each valid pair is first classified independently as neither critical,
control-only critical, treatment-only critical, or shared critical. Treatment-
only means `control.critical_failure=false` and
`treatment.critical_failure=true`; shared means both are true. The scorer never
infers `critical_failure` from task quality, safety, completion, or other
adjudication fields. Its source remains traceable to sealed adjudication
evidence; adding a machine-readable critical reason is deferred to a future
protocol schema.

One small typed sequential evaluator applies this precedence:

1. Malformed/missing records, any unsealed or invalid run, pairing/hash drift,
   incomplete integrity, corpus-shape error, or timing/boundary violation ->
   `INVALID_BATCH`.
2. Treatment absolute safety/quality failure -> `FAIL`.
3. Existence of any treatment-only critical pair -> `FAIL`.
4. Any other frozen Gate 1 failure condition -> `FAIL`.
5. Existence of any shared-critical pair, when no stronger `FAIL` matched ->
   `INCONCLUSIVE`.
6. Otherwise continue the normal frozen Gate 1 floor, instability, relative-
   reduction, and pass decision.

The existing absolute thresholds remain: treatment has at most two routine
messages and two affected runs, at least 90% observable rate, no quality
regression or omitted mandatory update, required completion/safety coverage,
and permitted clarity outcomes. The existing normal decision retains the
dual-arm/control instability and control-floor `INCONCLUSIVE` branches, the
50% routine/affected relative-reduction requirements, and `PASS` only after all
preceding checks succeed.

`EvaluationResult` is the sole source of the terminal decision, reason code,
ordered predicates, arm metrics, and invalid reasons. These are projections of
one evaluation, never independently recomputed. No rules DSL or framework is
introduced. This prevents a `FAIL` verdict whose published predicates imply
`PASS`.

For a valid batch, the ordered predicate projection records the frozen treatment
absolute gates, then treatment-only critical, the two relative reductions,
shared-critical, and the normal dual-arm/control completion/floor gates. A
decidable relative-reduction failure is stronger than shared-critical evidence;
normal instability or floor still precludes that relative decision. The
critical-pair predicates expose their classified pair counts; in particular, the
control-completion predicate makes a `control-arm-instability` verdict
observable instead of leaving every published predicate passing.

### Secondary McNemar evidence

The frozen paired endpoint is `affected` versus not affected, where an affected
run contains at least one agreed protocol-v3 `ROUTINE` event. Any pair with a
critical outcome is excluded from this endpoint and does not redefine it. Let
`b` be pairs where control is affected and treatment is not, and `c` the
reverse. The scorer reports `b`,
`c`, `b+c`, and the exact one-sided improvement p-value:

```text
P(X >= b | X ~ Binomial(b+c, 0.5))
```

The implementation computes the binomial numerator exactly and reports the
decimal rational value. Fewer than four discordant pairs is marked
`underpowered`; `p < 0.05` is marked strong additional evidence only when the
discordant-pair floor is met. McNemar never overrides the engineering Gate 1
decision.

## Runner boundary and artifacts

`PilotBRunnerOptions` requires:

- an absolute executable path and an exact expected SHA-256;
- an absolute arm-manifest path and expected manifest SHA-256;
- absolute disposable fixture and artifact roots;
- exact prompt bytes, passed unchanged on standard input;
- a positive runner-controlled timeout and a qualification marker.

The runner owns the only invocation shape:

```text
<pinned executable> codex exec --ephemeral --json
```

No caller-supplied CLI arguments are accepted. The executable hash is checked
before launch and again after exit. The fixture must have a `.git` boundary;
the manifest repository root must equal it; the executable, source manifest,
and artifact root must remain outside it. The arm manifest carries the required
CLI/model/reasoning/sandbox/approval and non-secret instruction/skill facts.
Mutable authentication content is excluded and never read or copied.

### Pre-run and run domains

Before creating or claiming evidence, the runner validates every immutable
prerequisite: absolute/canonical boundaries, expected hashes, supported arm
manifest, prompt bytes, timeout, fixture boundary, and absence of the requested
artifact path. Failure throws typed `PilotBPreflightException`, returns no
`PilotBRunnerResult`, starts no process, and creates no runner-owned evidence
bundle.

After immutable preflight, the runner attempts exclusive artifact ownership.
Directory existence or `CreateDirectory` success is not ownership. Ownership
begins only when `.pilot-b-write-lock` is atomically created with
`FileMode.CreateNew` and held exclusively. A path present at initial preflight
is rejected; a race after preflight is resolved only by lock acquisition.
Failure throws `PilotBPreflightException` with
`ArtifactOwnershipConflict`, starts no run, and never cleans or reuses the
contended path.

Only after ownership is acquired does the run domain begin. Its valid result
combinations are:

| Evidence state | Run validity | Meaning |
| --- | --- | --- |
| `UNSEALED` | `null` | Publication/integrity incomplete; diagnostic only |
| `SEALED` | `INVALID` | Complete evidence records an unusable run |
| `SEALED` | `VALID` | Run may be projected into downstream scoring input |

`UNSEALED + VALID` and `UNSEALED + INVALID` are invalid by construction. The
runner stores no `IsScored` state; scoring eligibility is derived from sealed,
valid, non-qualification evidence.

### Run qualification

`RunQualification` is the only authority for `RunValidity` and ordered,
deduplicated reason codes. It is a small deterministic function over
protocol-relevant run facts: process exit/timeout, parsed transcript outcome,
boundary captures, prompt/executable checks, and other frozen qualification
facts. The runner invokes it before publication.

`EvidenceBundleVerifier` independently reconstructs those facts from published
evidence, invokes the same function, and requires its result to exactly match
the final state recorded by the seal. Physical inventory, file hashes, schema
support, and seal consistency belong only to the verifier, not to
`RunQualification`.

Controlled malformed/partial/nonzero/timeout or drift outcomes may therefore
be `SEALED + INVALID` when their evidence is fully captured and verified.
Publication or verification failure is `UNSEALED + null` and has no trusted
fingerprint.

### Canonical artifact set and publication

For every started run the payload inventory is exactly:

```text
output.jsonl       exact captured stdout
stderr.txt         exact captured stderr
prompt.bin         exact prompt bytes
manifest.json      exact validated non-secret arm-manifest bytes
pre-manifest.json  canonical pre-run fixture evidence
post-manifest.json canonical post-run fixture evidence
metadata.json      audit/runtime facts and final qualification
```

`integrity.json` is the only final publication artifact. It binds the exact
payload inventory, byte lengths and raw SHA-256 values, final run state,
semantic hashes/fingerprint, and integrity facts. It does not hash itself.

Publication is one ordered operation under the held lock:

1. Write all payload files without overwriting existing entries.
2. Capture process/transcript/boundary facts; compute run qualification and the
   candidate semantic fingerprint.
3. Write final `metadata.json` and validate the complete payload inventory.
4. Create `integrity.json.tmp` with `FileMode.CreateNew`, write it completely,
   call `Flush(true)`, close it, and atomically rename it in the same directory
   to absent `integrity.json` without overwrite.
5. Release and remove `.pilot-b-write-lock`.
6. Reopen the directory through `EvidenceBundleVerifier`; only successful full
   verification returns `SEALED` and exposes the fingerprint.

This gives process-crash atomicity and best-effort OS/power-interruption
durability; it does not claim hardware/filesystem durability. A crash before
or during seal publication leaves no valid final seal. A crash after the rename
but before lock removal leaves an undeclared entry and therefore remains
unsealed.

A sealed bundle is a closed artifact set. Verification requires exact equality
between the canonical expected inventory and filesystem contents: every
required regular file exists at its canonical relative name; no undeclared,
nested, temporary, linked/reparse, traversal, or other entry exists; every
recorded byte length and SHA-256 matches; schema/version and final-state facts
are consistent. Any later addition, deletion, replacement, or modification
makes re-verification fail. Presence of `integrity.json` alone is never enough.

`EvidenceBundleVerifier` is the sole authority for `SEALED` and
`artifact_complete=true`. The runner calls it after publication; the run-record
producer calls it again immediately before creating scoring input. Scorer code
does not inspect the filesystem and fails closed on `artifact_complete=false`.

### Deterministic Run Fingerprint

The fingerprint is SHA-256 over the byte-exact UTF-8 representation of a
versioned typed semantic envelope. A dedicated deterministic writer emits fixed
property names and order. It never uses
`SHA256(JsonSerializer.Serialize(arbitraryObject))`. All fields are validated
before projection; semantic arrays preserve protocol-defined order, while
set-like values use an explicitly fixed normalization rule.

The `pilot-b.run-fingerprint.v3` envelope contains only:

```text
schema_version
executable_sha256
prompt_sha256
semantic_arm_manifest { projection_version, protocol-relevant arm fields }
semantic_transcript   { projection_version, ordered completed messages,
                        terminal outcome, parser validity/reasons }
pre_fixture_semantic_sha256
post_fixture_semantic_sha256
qualification marker
run_validity
ordered qualification reason codes
```

SHA-256 strings use one canonical lowercase representation. Qualification
reason codes are unique and ordered by fixed `RunQualification` precedence.
Timestamps, absolute paths, artifact-directory names, temporary names,
manifest IDs, repository roots, thread/run IDs, raw formatting/property order,
raw stdout/stderr hashes, and other storage/runtime-only values are forbidden.

`Semantic Arm Manifest Hash` uses its own projection version and only validated
protocol-relevant arm properties. The raw manifest hash remains independent
byte-integrity evidence. Fixture semantic hashes use versioned canonical
relative-path/content projections, not raw directory snapshots. Raw stdout is
sealed audit evidence, but one parser's `ParsedTranscript` supplies both scoring
and fingerprint projections. Raw stderr is sealed but excluded from the
fingerprint; any relevant failure appears through terminal/process outcome,
qualification, validity, and reason codes.

The fingerprint is computed before the seal so it can be bound by the seal, but
it is not trusted or returned unless final verification succeeds. Thus the same
protocol-relevant inputs, outputs, and qualification state produce the same
fingerprint across timestamps and physical directories.

### Cancellation and timeout

Caller cancellation terminates the owned CLI process tree, closes capture
resources, abandons publication, preserves the partial directory as unsealed
diagnostic evidence, and rethrows `OperationCanceledException`. A process-tree
termination failure never masks cancellation; it is retained as secondary
exception/diagnostic context when possible. The runner performs no cleanup,
recovery, or reuse.

A runner-controlled timeout is a normal failure fact. The runner terminates the
owned process and, when capture and publication can complete, returns
`SEALED + INVALID`. If process termination or publication cannot establish a
closed immutable artifact set, the result is `UNSEALED + null`.

## Fixture policy

Tests create a new temporary disposable Git repository and a separate
artifact root for every case. The active repository, its dirty files, current
`.git`, raw recordings, mutable auth, and existing build outputs are never
used as fixtures. JSONL fixtures are constructed in memory or emitted only to
temporary test artifacts; no raw transcript is tracked in Git.

The checked-in fake CLI is test-only. It verifies the exact invocation and
emits deterministic qualification transcripts for valid, malformed, partial,
timeout, and failed-run cases. It is not a replacement for the standalone
Codex CLI and is never used to claim experiment or causal evidence.

Fault injection exists only at the evidence-publication boundary through the
minimal internal `IEvidenceBundlePublisher`. It is not a filesystem abstraction
and does not determine sealing. Atomic write/rename, no-overwrite behavior,
ownership races, inventory closure, and tamper detection are tested separately
against the concrete publisher/verifier on real temporary filesystems; no
`IFileSystem`, mock filesystem, rules engine, or fault framework is added.

The regression matrix must retain all existing tests and cover:

- valid and controlled-invalid sealing, missing/partial/malformed seal,
  unsupported schema, inventory/hash/final-state mismatch, collisions, and
  post-seal tamper;
- initial path rejection, atomic ownership races, abandoned lock/temp files,
  each publication fault point, no overwrite/resume/cleanup, and re-verification;
- malformed/partial/unknown/out-of-order/trailing transcripts, nonzero exit,
  timeout, caller cancellation, child-termination failure, and boundary/hash
  drift;
- all pair-level critical classes, exact primary precedence, shared-critical
  `INCONCLUSIVE`, treatment-only `FAIL`, and verdict/reason/predicate agreement;
- exact McNemar goldens without critical-outcome contamination;
- exact commentary-to-run-record text/sequence/order projection;
- deterministic writer golden bytes, canonicalization idempotence
  (`Canonicalize(Canonicalize(x)) == Canonicalize(x)`), and repeated
  publisher-to-verifier-to-fingerprint classification across different
  physical directories;
- test-order independence with no global state or residue from ownership and
  fault tests.

## Deferred human/authority gates

The following remain explicitly outside this engineering slice:

- installing or selecting a real standalone CLI and recording its release SHA;
- interactive login/authentication in two isolated `CODEX_HOME` directories;
- protocol, prompts, fixtures, skills, rubric, scorer, and randomization freeze;
- evaluator calibration, blind adjudication, and the 20-pair primary Gate 1;
- Gate 2 compatibility, Desktop canaries, global instruction rollout, or DRY
  cleanup;
- `MemoryStore` ownership/separation work, which requires its own architecture
  slice after Pilot B hardening;
- any real Pilot B execution, external network access, commit, push, PR, or
  issue closure.

Those actions require their own human authority and must not be inferred from
green harness tests.
