# E2E v2: STRUCTURAL_CO (Revit 2025.4), outlines + schedules + dialog smoke

Bundle: `C:\Users\rzlaz\AppData\Local\FreeFieldExporter\out\run-20261120-232643\STRUCTURAL_CO_e2e.ffbundle`
Run: headless via the WPF dialog code path (`dialogSmoke=true`: ExportDialog constructed, default selections, same `RunExport` the Export button calls), work copy with fixtures `createFixtureSheets=true fixtureCount=2` (FF-101, FF-102, framing + column schedules, framing schedule placed on FF-101).

Result: 7 pages (FF-101, FF-102, S-101, 4 VIEW-* pages), 8 viewports, 203 elements, 6 links, 2 schedules. Validator: PASS (full report: e2e-structural-co-v2-validator.md).

## Independent PDF alignment (tools/scripts/verify-bundle-pdf.mjs)
Bbox corners / outline vertices vs the vector vertices of each page (<=1.5 pt):

```
뽆䘭㄰ㄠ†††††††††††灡来‱ㄹㅸ㠴㈠敬敭猨楮⁰慧攩‵㌠捯牮敲⁨楴猠ㄵ㔯㈱㈠⠷㌥⤠敬敭猠睩瑨‾㴲⁣潲湥牳‴㈠簠潵瑬楮敳‷㜠⡭慸⁰瑳‶⤠癥牴數⁨楴猠ㄹ〯ㄹ」੆䘭㄰㈠†††††††††††灡来‱ㄹㅸ㠴㈠敬敭猨楮⁰慧攩‴㔠捯牮敲⁨楴猠ㄲ㜯ㄸ〠⠷ㄥ⤠敬敭猠睩瑨‾㴲⁣潲湥牳″㐠簠潵瑬楮敳‶㠠⡭慸⁰瑳‴⤠癥牴數⁨楴猠ㄵ㠯ㄶ㈍੓ⴱ〱††††††††††††灡来‱ㄹㅸ㠴㈠敬敭猨楮⁰慧攩‱㌵⁣潲湥爠桩瑳‴㘸⼵㐰 㠷┩⁥汥浳⁷楴栠㸽㈠捯牮敲猠ㄳ㈠簠潵瑬楮敳‰ 浡砠灴猠〩⁶敲瑥砠桩瑳‰⼰ഊ噉䕗ⴳ〱彔䡉剄彆䱏佒††††⁰慧攠ㄴ〴砲㌶㘠敬敭猨楮⁰慧攩‷㐠捯牮敲⁨楴猠㈴㠯㈹㘠⠸㐥⤠敬敭猠睩瑨‾㴲⁣潲湥牳‶㈠簠潵瑬楮敳‷㐠⡭慸⁰瑳‴⤠癥牴數⁨楴猠㈹㘯㈹㘍੖䥅圭㐰ㅟ䙏啒呈彆䱏佒††††灡来‱㐰㑸㈳㘶⁥汥浳⡩渠灡来⤠㜴⁣潲湥爠桩瑳′㌶⼲㤶 㠰┩⁥汥浳⁷楴栠㸽㈠捯牮敲猠㔹⁼⁯畴汩湥猠㜴 浡砠灴猠㐩⁶敲瑥砠桩瑳′㤶⼲㤶ഊ噉䕗ⵃ伭㄰ㅟ䙉剓呟䙌住删††⁰慧攠ㄴ〴砲㌶㘠敬敭猨楮⁰慧攩‷㜠捯牮敲⁨楴猠ㄴ㜯㌰㠠⠴㠥⤠敬敭猠睩瑨‾㴲⁣潲湥牳‴㐠簠潵瑬楮敳‷㜠⡭慸⁰瑳‶⤠癥牴數⁨楴猠ㄹ㠯㌱」੖䥅圭吮伮彆住呉乇††††††灡来‱㐰㑸㈳㘶⁥汥浳⡩渠灡来⤠㐰⁣潲湥爠桩瑳‱㘰⼱㘰 ㄰〥⤠敬敭猠睩瑨‾㴲⁣潲湥牳‴〠簠潵瑬楮敳‴〠⡭慸⁰瑳‴⤠癥牴數⁨楴猠ㄶ〯ㄶ」﻿FF-101                       page 1191x842 elems(in page) 53 corner hits 155/212 (73%) elems with >=2 corners 42 | outlines 77 (max pts 6) vertex hits 190/190
FF-102                       page 1191x842 elems(in page) 45 corner hits 127/180 (71%) elems with >=2 corners 34 | outlines 68 (max pts 4) vertex hits 158/162
S-101                        page 1191x842 elems(in page) 135 corner hits 468/540 (87%) elems with >=2 corners 132 | outlines 0 (max pts 0) vertex hits 0/0
VIEW-301_THIRD_FLOOR         page 1404x2366 elems(in page) 74 corner hits 248/296 (84%) elems with >=2 corners 62 | outlines 74 (max pts 4) vertex hits 296/296
VIEW-401_FOURTH_FLOOR        page 1404x2366 elems(in page) 74 corner hits 236/296 (80%) elems with >=2 corners 59 | outlines 74 (max pts 4) vertex hits 296/296
VIEW-CO-101_FIRST_FLOOR      page 1404x2366 elems(in page) 77 corner hits 147/308 (48%) elems with >=2 corners 44 | outlines 77 (max pts 6) vertex hits 198/310
VIEW-T.O._FOOTING            page 1404x2366 elems(in page) 40 corner hits 160/160 (100%) elems with >=2 corners 40 | outlines 40 (max pts 4) vertex hits 160/160
```

Outlines: bottom-face outer loop (beams/columns/footings = 4-6 pts oriented rectangles, slabs = footprint); outline vertices land on PDF vertices for 97-100 % of vertices on all pages except CO-101 FIRST FLOOR (64 %, view with different graphics overrides).

## Schedules (tools/scripts/verify-schedules.mjs, cross-check with MCP get_schedule_data on port 7891)
* FF Structural Framing Schedule: 120 rows, cells identical to Revit's rendered text, in order; 120/120 rows bound to gid+uid, all gids in elements.json.
* FF Structural Column Schedule: 20 rows, same (3 fields in the pre-existing doc session; fixture now asks for Base/Top level too).
* Embedded: FF-101's PDF payload carries `schedules: [FF Structural Framing Schedule (120 rows)]`; other sheets none.
* Row-to-element binding matches visible cell text to element parameter strings; elements that display identical values are interchangeable (documented limitation, e.g. beams with the same type, length and level).

## Bugs found and fixed in this round
* `TableSectionData.GetCellText` returned empty text for calculated cells (Family and Type, Material); `ViewSchedule.GetCellText(SectionType.Body, r, c)` is correct. The column-heading row (body row 0) is skipped.
* `ViewSheet.Outline` grows when a tall schedule is placed beyond the titleblock, so the page no longer matched it. The reference box for the sheet-to-page formula is now the titleblock bbox (Outline only for titleblock-less sheets).
* Revision schedule inside the titleblock is a ScheduleSheetInstance: placement guard now checks for the specific schedule.
