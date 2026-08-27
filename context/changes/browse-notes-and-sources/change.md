---
change_id: browse-notes-and-sources
title: Przeglądanie własnych notatek i materiałów (S-03)
status: implementing
created: 2026-08-18
updated: 2026-08-27
archived_at: null
---

## Notes

- Przejmuje **F6** z przeglądu planu S-01c (`context/archive/2026-08-17-note-review-save/reviews/plan-review.md:105-115`) — jedyne ustalenie, które tamten plasterek zostawił otwarte: ani notatki, ani materiały nie mają indeksu, a `NavMenu` nie ma wejścia do żadnego z nich.
- Rozstrzyga otwarte pytanie roadmapy dla S-03 („osobna lista najwyższego poziomu czy dostęp głównie obok notatki") na **dwie osobne strony**: `/` = notatki, `/materials` = materiały.
- Odstępstwo wykryte przy implementacji fazy 1: provider SQLite (tylko testy) odmawia tłumaczenia `ORDER BY` po `DateTimeOffset`, więc listy nie dałyby się w ogóle przetestować mimo poprawnego zapytania. Rozwiązane wyłącznie po stronie testów — `tests/10xNotes.Tests/SqliteTestContext.cs` podmienia `IModelCustomizer` i przechowuje znaczniki czasu jako ticks. Model produkcyjny, LINQ i emitowany `ORDER BY` są nietknięte; Postgres sortuje `timestamptz` natywnie.

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->
