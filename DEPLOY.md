# Deploying v1.1.5 — Step by Step

The workflow wasn't triggering because GitHub Actions requires the
workflow YAML to already exist on your default branch (main) BEFORE
any tag or release event can activate it. Here's the exact order:

---

## Step 1 — Push the workflow file to main FIRST (no tag yet)

```bash
# Make sure you're on main and up to date
git checkout main
git pull

# Copy these two files into your repo:
#   .github/workflows/build-release.yml  (the new workflow)
#   manifest.json                         (with v1.1.5 in versions array)

git add .github/workflows/build-release.yml manifest.json
git commit -m "fix: add CI workflow to main branch for v1.1.5"
git push origin main
```

**After this push:** go to your repo's Actions tab.
You should see a "Build & Release Plugin" run start immediately
(it triggers on push to main). This is just a test build — it won't
create a release because there's no tag. But it proves the workflow works.

---

## Step 2 — Wait for that build to pass

Check the Actions tab. If the build fails because of a .csproj issue
or missing file, fix it and push again. The workflow will keep
triggering on every push to main until it's green.

---

## Step 3 — Tag and release v1.1.5

Option A — Git CLI (recommended):
```bash
git tag v1.1.5
git push origin v1.1.5
```
This triggers the workflow via the `push: tags: v*` rule.
The workflow will build AND create a GitHub Release with the zip attached.

Option B — GitHub UI:
Go to Releases → Draft a new release → tag = v1.1.5.
This triggers the workflow via the `release: created` rule.

Option C — Manual dispatch:
Go to Actions → Build & Release Plugin → Run workflow → enter "v1.1.5".

---

## Why it didn't work before

GitHub has a chicken-and-egg rule: a workflow file must exist on
the default branch before it can be triggered by ANY event (tags,
releases, manual dispatch). If you only had the workflow file in
a tag or release commit but not on main, GitHub simply ignores it.

The updated workflow also triggers on `push: branches: main`,
so the instant you push it, GitHub discovers it and registers it
in the Actions tab. After that, tag and release triggers work too.
