<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Przeglądanie własnych notatek i materiałów (S-03)

- **Plan**: `context/changes/browse-notes-and-sources/plan.md`
- **Scope**: Phases 1-2 of 2 (wszystkie fazy)
- **Date**: 2026-08-27
- **Verdict**: NEEDS ATTENTION → wszystkie ustalenia naprawione 2026-08-27
- **Findings**: 0 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Kryteria automatyczne (1.1-1.5, 2.1-2.5) uruchomione i zielone, łącznie z tymi weryfikowalnymi strukturalnie: zero nowych plików w `Migrations/`, `Home.razor` nie istnieje, żadna nowa strona nie deklaruje `@rendermode`, żadna nie wstrzykuje `DbContext` ani `IDbContextFactory<>`. Kryteria manualne 2.6-2.14 są otwarte i uczciwie nieodhaczone — implementacja szła w worktree bez bazy i portów.

Izolacja właściciela zweryfikowana wobec **wygenerowanego SQL**, nie wobec komentarzy: filtr `owner_id = @ef_filter__CurrentUserId` ląduje po obu stronach `LEFT JOIN`. Zero ręcznych filtrów właściciela, zero `IgnoreQueryFilters()`. Projekcja potwierdzona — ani `content`, ani `draft_content` nie pojawia się w żadnej liście SELECT. Zero `MarkupString` w plasterku.

Wszystkie dziesięć guardraili z `## What We're NOT Doing` trzyma, każdy z dowodem.

## Findings

### F1 — Tożsamość na static SSR wisi na nieprzetestowanym niezmienniku DI

- **Severity**: WARNING
- **Impact**: MEDIUM — realny kompromis; zatrzymaj się i przemyśl
- **Dimension**: Safety & Quality
- **Location**: `Program.cs:125-129`
- **Detail**: Na static SSR `EndpointHtmlRenderer` wpycha `HttpContext.User` przez `IHostEnvironmentAuthenticationStateProvider`, a `AuthenticationStateCurrentUserAccessor.cs:21` odczytuje go przez `AuthenticationStateProvider`. Obie nazwy trafiają na tę samą instancję `SessionCapAuthenticationStateProvider` **tylko dlatego**, że `Program.cs` rejestruje typ konkretny raz i kieruje na niego oba interfejsy. Komentarz nad tą rejestracją sam nazywa stawkę: "two instances would leave every circuit anonymous while looking correctly wired". Te dwie strony to pierwsze uwierzytelnione strony static SSR, które czytają wiersze użytkownika — jeśli rejestracje się kiedyś rozjadą, zalogowany użytkownik z danymi zobaczy "Nie masz jeszcze żadnej zapisanej notatki". Awaria jest fail-closed, ale nieodróżnialna od utraty danych. `CurrentUserAccessorTests.cs:169` używa `StubAuthenticationStateProvider` i nigdy nie rozwiązuje realnego kontenera. To reguła z `lessons.md` #2 w nowym przebraniu: gwarancja przechodząca przez warstwy, trzymana wyłącznie komentarzem.
- **Fix**: Dodać test kontenera: `Assert.Same(sp.GetRequiredService<AuthenticationStateProvider>(), sp.GetRequiredService<IHostEnvironmentAuthenticationStateProvider>())`.
- **Decision**: FIXED

### F2 — Plan deklaruje indeks, którego nie ma

- **Severity**: WARNING
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Plan Adherence
- **Location**: `context/changes/browse-notes-and-sources/plan.md:261`
- **Detail**: Plan pisze: "Oba zapytania filtrują po `owner_id`, dla którego obie tabele mają indeks." Tabela `notes` **nie ma** indeksu po `owner_id`. Zweryfikowane w migracjach: `20260817200154_AddNoteAndNoteEvent.cs` zakłada wyłącznie `ix_note_events_owner_id_occurred_at` i `ix_notes_source_material_id` (unikalny). `source_materials` dostało swój indeks właścicielski w S-01a; `notes` nigdy. Oba zapytania skanują `notes` w poprzek wszystkich kont, potem sortują. Bez znaczenia przy `data_volume: small`, ale zdanie obiecuje gwarancję, której nie ma — a to jest akapit, na który ktoś się powoła, decydując, kiedy dołożyć paginację.
- **Fix**: Poprawić zdanie w planie; przy progu opisanym w tym samym akapicie dołożyć indeks `(owner_id, updated_at)` na `notes` — precedens `ix_note_events_owner_id_occurred_at` istnieje w repo dla dokładnie tego kształtu zapytania.
- **Decision**: FIXED

### F3 — Plan opisuje wiersz materiału sprzed integracji z S-02

- **Severity**: WARNING
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Plan Adherence
- **Location**: `context/changes/browse-notes-and-sources/plan.md:108,188`
- **Detail**: Kontrakt `SourceMaterialListItem` w planie nadal brzmi `(Guid Id, string Title, string OriginalFileName, DateTimeOffset CreatedAt, Guid? NoteId)` — bez `Kind` i z nienullowalną nazwą pliku. Opis wiersza (`:188`) przepisuje bezwarunkowe "Zaimportowano \<data\> z pliku `<code>`". Oba wpisy powstały przed S-02 i zostały świadomie zastąpione przez `e2ed95a`, bo inaczej dwa plasterki nie kompilowały się razem. Kto przeczyta plan obok kodu, odczyta to jako drift — a to plan jest nieaktualny.
- **Fix**: Back-port obu miejsc do planu, jak `d61e8e1` w poprzednim plasterku.
- **Decision**: FIXED

### F4 — Front door aplikacji bez obsługi błędu bazy

- **Severity**: OBSERVATION
- **Impact**: MEDIUM — realny kompromis; zatrzymaj się i przemyśl
- **Dimension**: Safety & Quality
- **Location**: `Components/Pages/Notes/Index.razor:56-60`, `Components/Pages/Materials/Index.razor:70-75`
- **Detail**: Brak `try`/`catch` na granicy bazy. Na static SSR odpowiedź nie jest jeszcze zrzucona, więc awaria rozwija się do `UseExceptionHandler("/Error")` (`Program.cs:210`) i użytkownik dostaje `Error.razor` — szablonową, anglojęzyczną stronę w polskiej aplikacji. To jest zgodne z istniejącym precedensem (`Notes/Detail.razor` i `Materials/Detail.razor` też czytają bez lokalnego catcha), więc nie jest złamaniem wzorca. Zmieniła się jednak stawka: `/` jest teraz stroną, na którą użytkownik ląduje po zalogowaniu, więc chwilowa awaria bazy staje się drzwiami wejściowymi produktu, a nie jednym widokiem szczegółów.
- **Fix**: Albo przyjąć świadomie, albo złapać i wyrenderować polski komunikat inline, jak `Notes/Detail.razor:161` robi to przy zapisie.
- **Decision**: FIXED

### F5 — `SqliteTestContext` jest wierny, ale w trzech miejscach bardziej pobłażliwy niż produkcja

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Architecture
- **Location**: `tests/10xNotes.Tests/SqliteTestContext.cs:58-64`
- **Detail**: Podmiana na ticks jest sama w sobie poprawna i test-only — zweryfikowane trzema drogami: typ jest `internal` w assembly testów, konwerter wchodzi przez `ReplaceService` na opcjach budowanych wewnątrz helpera (nie przez `OnModelCreating`), a `Migrations/` i `Data/` są w tym zakresie nietknięte. `UtcTicks` i `timestamptz` są oba uporządkowane po instancie, więc `ORDER BY` się zgadza. Trzy rozbieżności, żadna dziś nieosiągalna: (1) **precyzja** — tick to 100 ns, `timestamptz` to 1 µs, więc wiersze odległe o 100-900 ns są ściśle uporządkowane w SQLite, a remisują w Postgresie; testy używają odstępów godzinowych lub dokładnej równości, a `Id` i tak domyka remis; (2) **NULL-e** — konwerter obsługuje `DateTimeOffset?`, ale model nie ma nullowalnego znacznika czasu, więc gałąź jest martwa; gdyby powstał, Postgres na `DESC` daje NULLS FIRST, a SQLite ostatnie — dokładnie odwrotnie, i nic by tego nie zgłosiło; (3) **offset** — konwerter przyjmuje dowolny `Offset`, a Npgsql **rzuca** przy zapisie niezerowego offsetu do `timestamptz`; dziś każdy zapis idzie z `TimeProvider.GetUtcNow()`, ale harness jest bardziej permisywny niż produkcja. Co testy realnie dowodzą: że LINQ wyraża właściwą kolejność. Czego nie dowodzą: że Npgsql to tłumaczy — i nic w repo tego nie dowodzi, co plan przyjmuje wprost (`plan.md:246`).
- **Fix**: Dopisać te trzy zastrzeżenia do komentarza klasy — szczególnie to, że wymóg UTC egzekwuje produkcja, nie harness.
- **Decision**: FIXED

### F6 — Dwa kształty harnessu SQLite obok siebie

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Pattern Consistency
- **Location**: `tests/10xNotes.Tests/CurrentUserAccessorTests.cs:149`, `GenerationQuotaTests.cs:166`, `GenerationQuotaConcurrencyTests.cs:98,113`, `OwnerScopingTests.cs:378`
- **Detail**: `SqliteTestContext` wchodzi jako wspólny harness, ale cztery istniejące klasy nadal budują opcje ręcznie. Każda trzyma własne połączenie, więc nie ma niezgodności schematu ani konwertera i nic nie jest zepsute — plan świadomie wykluczył refaktor nietkniętego kodu. Pozostałość to dwa kształty harnessu przechowujące `DateTimeOffset` inaczej (TEXT kontra INTEGER), więc następna osoba dopisująca test kolejności do jednej z tych czterech trafi na ten sam błąd tłumaczenia i może rozwiązać go po raz drugi.
- **Fix**: Przenieść je w osobnym commicie `chore:` albo nazwać ten podział w komentarzu klasy.
- **Decision**: FIXED

### F7 — Cztery nazwy na jedną stronę

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Pattern Consistency
- **Location**: `Components/Pages/Materials/Index.razor:20`
- **Detail**: `/materials/import` nazywa się dziś na cztery sposoby: "Dodaj materiał" (`NavMenu.razor:30`), "Zaimportuj materiał" (tutaj), "Dodaj nowy materiał" (`Notes/Detail.razor:30` i `Materials/Detail.razor`). Częściowo zastane — S-02 przemianował trzy miejsca, ten plasterek dokłada czwarte brzmienie.
- **Fix**: Wybrać jeden czasownik i ujednolicić wszystkie cztery.
- **Decision**: FIXED

### F8 — Wymóg projekcji nie ma egzekutora

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Success Criteria
- **Location**: `Notes/NoteService.cs:70`, `SourceMaterials/SourceMaterialService.cs:55`
- **Detail**: Plan nazywa projekcję "wymogiem, nie optymalizacją" (`plan.md:40`), bo `SourceMaterial.Content` to nieograniczony `text`. Projekcja jest poprawna przez konstrukcję — `Select` poprzedza `ToListAsync` — ale żaden test nie asertuje, że wygenerowany SQL pomija kolumny treści. Refaktor, który zmaterializowałby encje przed projekcją, przeszedłby całą suitę.
- **Fix**: Asercja na `ToQueryString()` sprawdzająca, że `content` i `draft_content` nie występują w żadnym z dwóch zapytań.
- **Decision**: FIXED
