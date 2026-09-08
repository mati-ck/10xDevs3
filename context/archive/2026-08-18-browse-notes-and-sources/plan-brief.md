# Przeglądanie własnych notatek i materiałów (S-03) — Plan Brief

> Full plan: `context/changes/browse-notes-and-sources/plan.md`

## What & Why

Zalogowany użytkownik dostaje listę swoich zapisanych notatek i listę swoich materiałów źródłowych, i z każdej otwiera wybrany element (FR-007). Powód jest ostrzejszy niż samo listowanie: aplikacja **nie ma dziś wejścia** do własnych treści — po opuszczeniu przeglądarki zapisana notatka jest nieosiągalna bez wklejenia adresu z ręki. Ten plasterek jest drzwiami do grafu treści.

## Starting Point

Po S-01c pętla import → generowanie → zapis działa w całości, ale `NavMenu` oferuje tylko Home / „Zaimportuj materiał" / Wyloguj, `/materials/{id}` osiąga się jedynie przekierowaniem po imporcie, a `/notes/{id}` przekierowaniem po zapisie. Obie strony linkują do siebie nawzajem wewnątrz wysepki bez drzwi. To ustalenie F6 z przeglądu planu S-01c — jedyne, które tamten plasterek zostawił otwarte i przekazał tutaj wprost. `Components/Pages/Home.razor` to nadal 7 linii szablonu („Hello, world!"), więc trasa `/` jest martwa. Model danych jest gotowy: `Note` i `SourceMaterial` mają wszystko, czego wiersz listy potrzebuje, łącznie z indeksem na `OwnerId` założonym w S-01a wprost pod to zapytanie.

## Desired End State

Użytkownik wchodzi na `/` i widzi **Moje notatki** — najnowszy zapis na górze, każdy wiersz z tytułem, datą zapisu i linkiem do materiału źródłowego. Na `/materials` widzi **Moje materiały** — najnowszy import na górze, z nazwą pliku i, gdy notatka istnieje, linkiem do niej. Świeże konto dostaje na obu stronach komunikat mówiący, co zrobić dalej, z linkiem tam, gdzie to zrobić. Drugie konto nie widzi ani jednego cudzego wiersza.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Struktura listy | Dwie osobne strony | Rozstrzyga otwarte pytanie roadmapy dla S-03; materiał dostaje własną listę najwyższego poziomu, nie tylko miejsce obok notatki. |
| Wejście | `/` to lista notatek, `/materials` to lista materiałów | Jedna trasa na stronę, bez przekierowania, a front door produktu to jego wynik — notatka. |
| Los `Home.razor` | Usunięty | `@page "/"` może istnieć raz, a to ostatnia strona nietknięta od szablonu. |
| Zawartość wiersza | Tytuł + data + link na drugą stronę relacji | Skanowalne, bez czytania kolumn treści, i utrzymuje gwardrail PRD (źródło obok notatki) widoczny już z listy. |
| Objętość | Pełna lista, najnowsze na górze, bez paginacji | PRD ustawia `data_volume: small`; paginacja byłaby rusztowaniem wokół kilkudziesięciu wierszy. |
| Usuwanie | Poza zakresem | To S-05, a usuwanie materiału wymaga rozstrzygnięcia OQ2, które celowo blokuje S-06. |
| Stan pusty | Osobna kopia na każdej stronie | Pierwszy ekran nowego konta musi mówić, co zrobić dalej — to jest sens tego plasterka. |
| Zakres nawigacji | Tylko wpisy w `NavMenu` | Linki powrotne na stronach szczegółów, przekierowanie paneli „nie znaleziono" i sprzątanie linku „About" odrzucone świadomie. |
| Tryb renderowania | Static SSR, bez `@rendermode` | Nie ma tu nic interaktywnego, a obwód kazałby odpytać bazę dwa razy (prerender + circuit). |
| Szew testowalny | Metody `ListAsync` na serwisach + rekordy projekcji | Brak bUnit oznacza, że cokolwiek ma być udowodnione, musi mieszkać poza komponentem. |

## Scope

**In scope:**
- `NoteListItem` / `SourceMaterialListItem` — pierwsze typy projekcji w projekcie
- `NoteService.ListAsync` i nowy `SourceMaterials/SourceMaterialService.ListAsync` (lewostronne złączenie po obecność notatki)
- `Components/Pages/Notes/Index.razor` pod `/` i `Components/Pages/Materials/Index.razor` pod `/materials`, oba static SSR
- Polskie stany puste z linkiem do następnego kroku
- Dwa wpisy w `NavMenu`; usunięcie `Home.razor`
- Testy SQLite: izolacja właściciela, kolejność, obecność notatki

**Out of scope:** wyszukiwanie, filtry i sortowanie z UI; paginacja i `Virtualize`; usuwanie (S-05/S-06); podgląd treści w wierszu; linki powrotne na stronach szczegółów; przekierowanie paneli „nie znaleziono"; usuwanie szablonowego linku „About"; refaktor istniejących bezpośrednich zapytań o materiał; landing page dla niezalogowanych; jakakolwiek migracja.

## Architecture / Approach

Dwie warstwy. **Zapytania**: dwie metody zwracające `IReadOnlyList<T>` rekordów projekcji, oba serwisy przez `UserScopedDbContextFactory`, **bez ani jednego ręcznego filtru właściciela** — globalny filtr `AppDbContext` jest dowodem prywatności, a `CLAUDE.md` zabrania per-query filtrów. Projekcja nie niesie żadnej kolumny treści. **Widok**: dwie strony static SSR, jedno zapytanie na wejście, daty przez `TimeProvider.ToDisplayTime` z kulturą `pl-PL`, tytuły renderowane jako tekst (żadnego `MarkupString`). Wejście domyka podmiana `/` i dwa wpisy w nawigacji.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Warstwa zapytań | Projekcje, dwie metody `ListAsync`, rejestracja w DI, testy | Lewostronne złączenie to pierwszy join między dwiema tabelami aplikacji; musi być filtrowany po obu stronach i nie może rozmnożyć wierszy |
| 2. Strony list i wejście | Dwie strony static SSR, stany puste, `NavMenu`, usunięcie `Home.razor` | Static SSR to pierwsza taka ścieżka czytająca bazę w tym projekcie; podmiana `/` dotyka trasy szablonowej |

**Prerequisites:** S-01c i F-02 są `done` — nic nie blokuje. Żadnej migracji, żadnego nowego pakietu, żadnego sekretu.
**Estimated effort:** ~1 sesja, dwie fazy.

## Open Risks & Assumptions

- Zapytania są nieograniczone. Świadome, wobec `data_volume: small`, ale konto z tysiącami materiałów przeczyta je wszystkie i wyrenderuje jedną bardzo długą stronę.
- Static SSR działa z warstwą danych na podstawie tego, co dokumentują `AuthenticationStateCurrentUserAccessor` i `Program.cs`; **żadna istniejąca strona static SSR nie czyta jeszcze bazy**, więc pierwsze ręczne przejście jest właściwą weryfikacją tego założenia.
- Tytuły notatek i materiałów są niemal zawsze identyczne (notatka dziedziczy tytuł materiału), więc lista notatek nie powtarza tytułu materiału. Gdyby użytkownicy zaczęli masowo zmieniać tytuły notatek, wiersz materiału może chcieć pokazywać tytuł notatki — dziś pokazuje tylko link.
- Kolejność listy notatek idzie po `UpdatedAt`, więc poprawka literówki przesuwa starą notatkę na górę. To celowe („ostatnio pracowane"), ale jest widocznym zachowaniem, nie szczegółem.
- `/` przestaje być publiczne. Produkt nie ma i nie zyskuje strony wejściowej dla anonima.

## Success Criteria (Summary)

- Użytkownik, który wczoraj zapisał notatkę, dziś po zalogowaniu widzi ją na pierwszym ekranie — bez wklejania adresu.
- Z każdego wiersza da się dojść do materiału i do notatki, więc źródło zostaje dostępne obok notatki (gwardrail PRD).
- Drugie konto nie widzi ani jednego cudzego wiersza, co jest pokryte testem, a nie tylko klikaniem.
