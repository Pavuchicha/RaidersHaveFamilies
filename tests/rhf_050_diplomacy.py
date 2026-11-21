"""Source contract checks, not a substitute for compiled DLL/in-game tests."""
from pathlib import Path
p=Path(__file__).resolve().parents[1]/'RaidersHaveFamilies/Source'
dlg=(p/'RHFDialogue.cs').read_text('utf-8');patch=(p/'RHFPatches.cs').read_text('utf-8')
host=(p/'RHFHostageSystems.cs').read_text('utf-8');debug=(p/'RHFDebugActions.cs').read_text('utf-8')
assert 'FactionDialogMaker.FactionDialogFor' in patch
assert 'RHFDialogue.HasCaptives(faction)' in patch
assert 'Building_CommsConsole), "GetFloatMenuOptions"' not in patch
assert 'Обсудить возвращение наших пленников' in patch
assert 'linkLateBind' in dlg and 'ReturnNode' in dlg and 'Постоянная вражда' in dlg
assert 'Суммы указаны в серебре' in dlg and 'CounterOfferChancePercent' in dlg
assert 'AgreeRelease' in host and 'ReportCondition' in host
assert 'LastConfirmedOperation' in host and 'Scribe_Values.Look(ref LastConfirmedOperation' in host
assert 'DropPodUtility.DropThingsNear' in host and 'new LookTargets(dropCenter, previousMap)' in host
assert 'ExitCell' in patch and 'OnPrisonerExit(__instance, __state.Map, true, __state.ExitCell)' in patch
assert 'SeverityOf(RecipeDef recipe)' in patch and 'recipe.isViolation' in patch
assert 'RHFWCERegistry.SeverityOf(recipe.defName)' in patch
assert 'RHF - ТЕСТЫ | РЕЙДЫ' in debug and 'RHF - ТЕСТЫ | ПЛЕННИКИ' in debug
assert 'DebugActionType.ToolMap' in debug and 'UI.MouseCell()' in debug
assert debug.count('public static void Rank') == 8
assert 'RHFHostageDiplomacy.RecordWceOperation' in debug
assert 'record.Importance = Mathf.Clamp(rank, 0, 8)' in debug
assert 'return awarded;' in host
print('PASS: 0.5 source contracts (vanilla radio, dialogue, evidence, reputation, pod reward, debug ranks).')
