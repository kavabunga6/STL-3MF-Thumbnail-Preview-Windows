# Render regression checks

The Windows workflow runs for pull requests, pushes to `main`/`master`, and manual dispatches.
It builds the PR merge result and its base commit, then runs the **same independent benchmark executable**
against both renderer DLLs on one `windows-2022` runner. Using the merge result also brings the
current test harness into PRs opened before the harness was added. Manual runs can set `candidate_ref`
to a ref such as `refs/pull/4/merge` and `baseline_ref` to `main`. The renderer is never installed or registered
with Explorer. The production renderer does not reference the benchmark project.

## What fails the check

- A renderer crashes, hangs, returns an error for a supported fixture, produces the wrong image size,
  or returns an image with fewer than eight coloured foreground pixels.
- A malformed fixture unexpectedly produces a thumbnail.
- Any fixture is changed, deleted, renamed, loses its read-only attribute, or gains a neighbouring file.
  SHA-256, file length, last-write time, attributes, and the complete fixture file list are checked
  after every worker, including workers terminated by timeout.
- Any individual render exceeds **5 seconds** (including the first call). An isolated worker is
  terminated after **45 seconds**, so a stuck renderer cannot hang the entire suite.
- A warm median is **more than 30% and more than 20 ms slower** than the baseline, and an independent
  repeat in the reverse execution order confirms the slowdown.

The initial thresholds are deliberately tolerant of shared-runner noise. Adjust them in `Models.cs`
after collecting CI results. A failed baseline is explicitly reported; a passing candidate can still pass.
A failed candidate always fails, regardless of the baseline. To enforce this check before merge, make
`Render correctness and performance` a required status check in the repository's branch rules.

## Measurements

Each worker starts a fresh process, performs two warmup renders and five measured renders, and reports
the warm median, p95, individual samples, first-call time, and peak process working set. Base/head order
alternates between cases. Suspected slowdowns are measured again in the opposite order.

The timed region includes provider creation, initialization, input opening, parsing, rendering and
HBITMAP creation. Fixture generation, hash checks, image validation and PNG encoding are excluded.
The first-call measurement includes first-use JIT/framework work; it is **not a cold-disk benchmark**.
These checks exercise the managed thumbnail provider directly, including the read-only Shell stream
path, without Explorer's thumbnail cache.

## Fixtures and images

The corpus is generated deterministically once per run, shared by both versions, and marked read-only.
It contains binary/ASCII STL (including a 750,000-triangle sphere), OBJ, ASCII PLY, AMF, OFF, 3MF
with/without an embedded preview, normal/tiny/tab-separated G-code, tessellated/wireframe STEP,
and malformed STL. Both `IInitializeWithFile` and `IInitializeWithStream` are tested.
All cases run at 256 px; binary cube, dense STL and normal G-code also run at 1024 px: **36 scenarios**.
No downloaded assets, private models, model sidecars or binary fixture files are required.

The job summary contains the timing comparison. Download `render-comparison` and open `index.html`
for baseline/candidate PNGs side by side; `report.json` contains the full results and input hashes.
Artifacts are retained for 14 days and uploaded even after a benchmark failure.

Visible-content checks catch blank output, but cannot prove correct geometry or orientation.
Pixel equality is deliberately not a pass/fail rule: legitimate rendering improvements change pixels.
Review the images when a renderer changes; add targeted functional tests for geometry-specific bugs.
These tests detect fixture mutation but do not sandbox arbitrary malicious code or prove that every
possible user file is safe. CI uses disposable GitHub-hosted runners with read-only repository access.

## Run locally

Build the baseline and candidate in separate checkouts with the same .NET 6 SDK on Windows x64.
From the candidate checkout:

```powershell
dotnet build Explorer3DPreview.sln -c Release -p:Platform=x64
dotnet build C:\src\baseline\Explorer3DPreview\Explorer3DPreview.csproj -c Release -p:Platform=x64
dotnet .\Explorer3DPreview.Benchmarks\bin\x64\Release\net6.0-windows\Explorer3DPreview.Benchmarks.dll --self-test
dotnet .\Explorer3DPreview.Benchmarks\bin\x64\Release\net6.0-windows\Explorer3DPreview.Benchmarks.dll `
  C:\src\baseline\Explorer3DPreview\bin\x64\Release\net6.0-windows\Explorer3DPreview.dll `
  .\Explorer3DPreview\bin\x64\Release\net6.0-windows\Explorer3DPreview.dll `
  .\artifacts\render-benchmarks baseline candidate
```

To check runner stability, pass the same renderer DLL as both inputs.
Output is written only to the designated report directory and synthetic test directories.
User-supplied models are not accepted as benchmark inputs. Each invocation creates a fresh fixture
directory and refuses to generate fixtures into an existing non-empty directory.

## Fork pull requests

The workflow uses `pull_request`, `contents: read`, no persisted checkout credentials, no repository
secrets, and no privileged `pull_request_target` event. A first-time contributor's run may require
the normal GitHub Actions approval. This workflow does not post PR comments or change branch rules.
