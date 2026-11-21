from pathlib import Path
p=Path(__file__).resolve().parents[1]
s=(p/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text()
d=(p/'RaidersHaveFamilies/Source/RHFDebugActions.cs').read_text()
x=(p/'RaidersHaveFamilies/Defs/ThoughtDefs/RHF_Thoughts.xml').read_text()
assert 'public static string EligibleTraitReason' in s
assert 'public static bool EligibleIdeology' in s
assert 'n.Equals("Bloodlust"' in s and 'n.Equals("Psychopath"' in s
assert 'n.Equals("Sadist"' in s and 'n.EndsWith("_Sadist"' in s
assert 'EligibleTraitReason(p, kind) == null && !EligibleIdeology(p, victim, kind)' in s
assert 'if (newCruelty <= 0 || RHFMod.Settings?.Enabled != true) return 0;' in s
assert 'moods = RHFMoralResponse.GivePublicCrueltyThoughts' in s
assert 'r.DisclosedCrueltyScore = r.CrueltyScore;' in s
assert 'return ReportCondition(r);' in s
assert 'EligibleTraitReason(colonist, RHFPublicityKind.Torture)' in d
assert 'EligibleIdeology(colonist, p, RHFPublicityKind.Torture)' in d
dialog=(p/'RaidersHaveFamilies/Source/RHFDialogue.cs').read_text()
assert 'Сообщить лично родственнику' in dialog
assert 'Сообщить представителю фракции о ранениях и операциях' in dialog
assert x.count('RHF: злорадство от чужого горя') == 6
print('PASS: reward is based on disclosure, OR of trait/ideology, one RHF memory, and Russian dev diagnostics')
