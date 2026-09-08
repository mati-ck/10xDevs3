---
change_id: browse-notes-and-sources
title: Przeglądanie własnych notatek i materiałów (S-03)
status: impl_reviewed
created: 2026-08-18
updated: 2026-09-08
archived_at: null
---

## Notes

- Przejmuje **F6** z przeglądu planu S-01c (`context/archive/2026-08-17-note-review-save/reviews/plan-review.md:105-115`) — jedyne ustalenie, które tamten plasterek zostawił otwarte: ani notatki, ani materiały nie mają indeksu, a `NavMenu` nie ma wejścia do żadnego z nich.
- Rozstrzyga otwarte pytanie roadmapy dla S-03 („osobna lista najwyższego poziomu czy dostęp głównie obok notatki") na **dwie osobne strony**: `/` = notatki, `/materials` = materiały.
- Odstępstwo wykryte przy implementacji fazy 1: provider SQLite (tylko testy) odmawia tłumaczenia `ORDER BY` po `DateTimeOffset`, więc listy nie dałyby się w ogóle przetestować mimo poprawnego zapytania. Rozwiązane wyłącznie po stronie testów — `tests/10xNotes.Tests/SqliteTestContext.cs` podmienia `IModelCustomizer` i przechowuje znaczniki czasu jako ticks. Model produkcyjny, LINQ i emitowany `ORDER BY` są nietknięte; Postgres sortuje `timestamptz` natywnie.
- Fazy 1 i 2 zaimplementowane (15e624a, 2fb17cd). Weryfikacja ręczna domknięta **2026-09-08**: wszystkie punkty 2.6-2.14 przechodzą, aplikacja uruchomiona lokalnie (`dotnet run --launch-profile http`) przeciwko realnej bazie.
- Cztery punkty czekały od 2026-08-27 na warunki, których tamta sesja nie miała: 2.6 i 2.7 wymagały świeżego konta, 2.12 drugiego konta, a 2.10 listy z więcej niż jedną notatką. Do 2.10 wystarczyło konto główne — ponowny zapis **najstarszej** z czterech notatek przesunął ją z pozycji 4/4 na 1/4; sierpniowa próba nie dowiodła przesunięcia, bo re-zapisywała notatkę już będącą na szczycie.
- Na potrzeby 2.6/2.7/2.12 założone dwa konta testowe w bazie deweloperskiej: `s03-fresh-0908@example.com` i `s03-drugie-0908@example.com`. Do usunięcia, gdy nie będą już potrzebne.
- Obserwacja poboczna, nie wpływa na kryteria: strony szczegółów renderują w prerenderze stan "Nie znaleziono notatki"/"Nie znaleziono materiału", zanim obwód SignalR wstanie i podmieni go na treść. Widoczne jako mignięcie przy wolnym łączu. Podobnie formularz wylogowania ignoruje klik wykonany przed podłączeniem obwodu.

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->
