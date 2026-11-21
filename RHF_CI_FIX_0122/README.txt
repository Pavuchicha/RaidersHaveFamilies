Raiders Have Families v0.12.2 — CI-FIX-1

1. Open GitHub Desktop > Repository > Show in Explorer.
2. Extract the RHF_CI_FIX_0122 folder into that project folder.
3. Double-click ИСПРАВИТЬ_СБОРКУ.bat.
4. If SUCCESS, return to GitHub Desktop. The only tracked change should be tests/rhf_040_contracts.py.
5. Commit to main, then Push origin. This runs the GitHub Actions checks again.
6. You may delete the RHF_CI_FIX_0122 folder afterward.

This is NOT a compiled release. The GitHub CI log showed a stale assertion expecting MaxPerWave and HardMaxPerCycle to be 16, when the new version has 48. The script refuses to touch other versions and changes only that test.
