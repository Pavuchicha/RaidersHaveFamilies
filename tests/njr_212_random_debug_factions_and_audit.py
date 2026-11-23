"""R005 source-contract audit for dev spawn faction sampler and R004 critical regressions.
These are STATIC checks + independent models, NOT compiled/gameplay evidence.
"""
from pathlib import Path
import re
import random
R=Path(__file__).resolve().parents[1]
S=R/'RaidersHaveFamilies/Source'
debug=(S/'RHFDebugActions.cs').read_text(encoding='utf-8')
ops=(S/'RHFOperations.cs').read_text(encoding='utf-8')
assert 'int nextTestFaction;' not in debug, 'Round-robin selector leaked into R005'
assert 'RandomTestFaction(List<Faction> candidates)' in debug
assert 'lastRandomFaction = chosen;' in debug
assert 'Rand.Range(0, choices.Count)' in debug
assert 'candidate != lastRandomFaction' in debug
assert 'if (choices.Count == 0) choices.AddRange(candidates);' in debug
assert 'ReferenceEquals(factionSelectionGame, Current.Game)' in debug
assert 'pinnedTestFaction = null;' in debug
assert 'return RandomTestFaction(candidates);' in debug
assert 'faction.def.pawnGroupMakers != null' in debug and 'option?.kind' in debug
assert 'faction.def.basicMemberKind' in debug
assert 'FindUnusedEnemyCandidate' in debug and 'return RandomTestFaction(candidates);' in debug
assert 'СЛУЧАЙНАЯ (снять закрепление)' in debug
assert 'ЗАКРЕПИТЬ следующую (не случайно)' in debug
assert 'EligibleTestFaction(faction, industrialOnly)' in debug
# Technology check precedes the charge in the standard incident path; reserve
# fallback is also explicitly edge-only for low-tech executors.
tech=ops.index('if(party.Drop && !NJRForceCompositionS42.CanUsePods(party.Faction))')
charge=ops.index('if (op.TryPod && party.Drop &&')
assert tech<charge
fallback=ops.split('RH FNONEXISTENT')[-1]
assert 'if(NJRForceCompositionS42.LowTech(alternate))' in fallback
assert 'parms.raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn;' in fallback

# Model the same RNG exclusion contract for 1/2/3/6 eligible factions.
for size in (1, 2, 3, 6):
    rng=random.Random(43717+size)
    history=[]
    for _ in range(1000):
        candidates=list(range(size))
        eligible=[c for c in candidates if c!=history[-1]] if size>1 and history else candidates
        chosen=rng.choice(eligible or candidates)
        assert chosen in candidates
        if history and size>1:assert chosen!=history[-1]
        history.append(chosen)
    if size>2:assert len(set(history))==size
# Visible install title, 18 debug scenarios, and unchanged save/assembly identity.
about=(R/'RaidersHaveFamilies/About/About.xml').read_text(encoding='utf-8')
assert '<name>NJR — Not Just Raiders</name>' in about
assert '<packageId>pavuk.raidshavefamilies</packageId>' in about
assert 'namespace RaidersHaveFamilies' in debug
assert len(re.findall(r'\[DebugAction\(Category, "\d\d —',debug))==18
print('PASS NJR R005: true random dev factions, anti-repeat, explicit pinned override, modded pawnGroupMakers and unchanged save identity (STATIC/MODEL).')
