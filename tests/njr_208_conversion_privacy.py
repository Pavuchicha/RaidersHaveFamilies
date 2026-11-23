"""NJR V15 faction intelligence boundary for Ideology conversion."""
from pathlib import Path
r=Path(__file__).resolve().parents[1]
s=(r/'RaidersHaveFamilies/Source/RHFHostageSystems.cs').read_text('utf-8')
record=s[s.index('public static void RecordConversion('):s.index('public static string ReportConversion(')]
report=s[s.index('public static string ReportConversion('):s.index('public static string ReportRecruitment(')]
assert 'QueueRetaliation(' not in record, 'Unreported conversion cannot cause clairvoyant retaliation'
assert 'r.IdeologyReported=true;' in report
assert 'QueueRetaliation(r,true);' in report
assert 'r.Importance>=6' in report
assert 'RHFPoliticalReputation.Change(r.OriginalFaction' in report
assert 'r.RecruitmentReported=true;' in s # distinct recruitment intelligence route
print('PASS V15 explicit-conversion-disclosure intelligence contract (STATIC)')
