# GitHub Actions Build-Artifacts CI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow that builds Windows and Android artifacts of the ArctZ Avalonia app on every push/PR to `master`, and push the local repo to the existing (empty) GitHub repo so the workflow actually runs.

**Architecture:** One workflow file, two independent parallel jobs (`build-windows` on `windows-latest`, `build-android` on `ubuntu-latest`), each ending in `actions/upload-artifact`. No shared state between jobs, no reusable actions/composite steps needed — this is small enough to be two flat job blocks in one YAML file.

**Tech Stack:** GitHub Actions (`actions/checkout@v4`, `actions/setup-dotnet@v4`, `actions/upload-artifact@v4`), .NET 10 SDK, dotnet Android workload.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-23-github-actions-build-artifacts-design.md`
- Triggers: `push` and `pull_request` targeting `master` only.
- Windows artifact: `dotnet publish` of `ArctZ.Desktop/ArctZ.Desktop.csproj`, `-c Release -r win-x64 --self-contained true`, unsigned.
- Android artifact: `dotnet build` of `ArctZ.Android/ArctZ.Android.csproj`, `-c Debug` (debug-signed APK, no custom keystore).
- iOS is explicitly out of scope for this workflow.
- Artifacts are plain `actions/upload-artifact` workflow artifacts (default 90-day retention) — no GitHub Releases involved.
- GitHub repo already exists and is empty: `https://github.com/NKuzichkin/arctz-2.git`. No remote is currently configured in the local repo (`z:\Jib S\Application\ArctZ`).
- Local repo already has 2 commits on `master` (initial commit `ccecae2`, design-doc commit `683a64c`) — this plan adds one more commit for the workflow file, then pushes.

---

### Task 1: Add the CI workflow file

**Files:**
- Create: `.github/workflows/build.yml`

**Interfaces:**
- Produces: a GitHub Actions workflow named `Build Artifacts` with jobs `build-windows` and `build-android`, each uploading one artifact (`ArctZ-Desktop-win-x64`, `ArctZ-Android-debug-apk`). No other task in this plan depends on the internal job structure — Task 2 only depends on this file existing and being committed.

- [ ] **Step 1: Write the workflow file**

Create `.github/workflows/build.yml` with exactly this content:

```yaml
name: Build Artifacts

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

jobs:
  build-windows:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Publish ArctZ.Desktop (win-x64, self-contained)
        run: dotnet publish ArctZ.Desktop/ArctZ.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

      - uses: actions/upload-artifact@v4
        with:
          name: ArctZ-Desktop-win-x64
          path: publish/win-x64

  build-android:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Install Android workload
        run: dotnet workload install android

      - name: Build ArctZ.Android (Debug, debug-signed apk)
        run: dotnet build ArctZ.Android/ArctZ.Android.csproj -c Debug

      - uses: actions/upload-artifact@v4
        with:
          name: ArctZ-Android-debug-apk
          path: ArctZ.Android/bin/Debug/net10.0-android/*Signed.apk
```

- [ ] **Step 2: Sanity-check the YAML by hand**

No YAML linter is available in this environment (no `python3`/`node`/`yamllint` on PATH), so verify manually:
- Every `- uses:` / `- name:` list item is indented exactly 6 spaces under its job's `steps:` (2 for the job name under `jobs:`, 4 for `steps:` under the job, 6 for list items under `steps:`).
- `run:` blocks are single-line (no ambiguous multi-line indentation).
- The two job keys (`build-windows`, `build-android`) are siblings, each indented 2 spaces under `jobs:`.

The real validation happens in Task 3 when GitHub parses it — if the syntax is invalid, the push in Task 2 will show a red "workflow" check on GitHub with a parse error instead of running.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "Add GitHub Actions CI for Windows and Android build artifacts"
```

---

### Task 2: Connect the GitHub remote and push

**Files:** none (git operations only)

**Interfaces:**
- Consumes: the commit from Task 1 must exist on local `master` before this task runs.
- Produces: local `master` pushed to `origin/master` on `https://github.com/NKuzichkin/arctz-2.git`, triggering the workflow from Task 1 via its `push` trigger.

- [ ] **Step 1: Add the remote**

```bash
cd "/z/Jib S/Application/ArctZ"
git remote add origin https://github.com/NKuzichkin/arctz-2.git
git remote -v
```

Expected: shows `origin` with the fetch and push URLs pointing at `https://github.com/NKuzichkin/arctz-2.git`.

- [ ] **Step 2: Push master**

```bash
git push -u origin master
```

Expected: push succeeds (`* [new branch] master -> master`, `branch 'master' set up to track 'origin/master'`). This step needs the user's GitHub credentials (credential manager / cached token) to be available in the shell — if it prompts for auth or fails with a permission error, stop and report back rather than trying alternate auth methods.

- [ ] **Step 3: Confirm the push landed**

```bash
git log --oneline origin/master -3
```

Expected: shows the same 3 commits as local `master` (`ccecae2`, `683a64c`, and the Task 1 workflow commit), confirming `origin/master` now matches local `master`.

---

### Task 3: Verify the workflow actually ran and produced both artifacts

**Files:** none (verification only)

**Interfaces:**
- Consumes: the push from Task 2 must have happened (this task polls the run it triggered).

- [ ] **Step 1: Find the triggered run**

```bash
curl -s "https://api.github.com/repos/NKuzichkin/arctz-2/actions/runs?branch=master&event=push&per_page=1"
```

Expected: JSON with `workflow_runs[0]` present, `head_commit.message` matching the Task 1 commit message, and `status` either `in_progress` or `queued`.

- [ ] **Step 2: Poll until the run completes**

Re-run the same `curl` command every ~30 seconds (this is a real external CI run — expect several minutes, since `build-android` downloads the .NET Android workload from scratch) until `workflow_runs[0].status` is `completed`.

Expected final state: `workflow_runs[0].conclusion` is `"success"`.

If `conclusion` is `"failure"`: fetch the run's jobs to see which one failed —

```bash
curl -s "https://api.github.com/repos/NKuzichkin/arctz-2/actions/runs/<run_id>/jobs"
```

— report the failing job name and step back to the user rather than guessing at a fix; this plan does not cover CI debugging.

- [ ] **Step 3: Confirm both artifacts were produced**

```bash
curl -s "https://api.github.com/repos/NKuzichkin/arctz-2/actions/runs/<run_id>/artifacts"
```

Expected: `artifacts` array contains exactly two entries named `ArctZ-Desktop-win-x64` and `ArctZ-Android-debug-apk`, both with `expired: false`.

- [ ] **Step 4: Report the result to the user**

Summarize: run URL (`https://github.com/NKuzichkin/arctz-2/actions/runs/<run_id>`), both artifact names, and confirmation that both jobs succeeded. No commit needed for this task — it's read-only verification.
