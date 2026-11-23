# NJR R015 — CI hotfix, 23.11.2026

**GitHub Actions run #39:** https://github.com/Pavuchicha/RaidersHaveFamilies/actions/runs/45231879278

- Job `compile-and-validate`, step `Compile mod DLL`: FAIL
- `RHFRescueControlV123.cs:412`: CS0103, variable `printed` does not exist.
- Replace ONLY the diagnostic text using existing persisted field `op.CreatedTick`.
- No gameplay algorithm or save keys changed. This fixes the compiler diagnostic found in #39, and does not establish that the resulting game runs correctly.
- The connected GitHub integration rejected the attempted direct write with 403 `Resource not accessible by integration`; apply by GitHub Desktop.
- Install: Repository → Show in Explorer → unpack cumulative ZIP into repo root, replace files → Commit to main → Push origin.
- For a one-file update, the smaller `NJR_R015_HOTFIX_GITHUB_CS0103.zip` is sufficient.
