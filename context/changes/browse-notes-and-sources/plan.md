# Przeglądanie własnych notatek i materiałów (S-03) — Implementation Plan

## Overview

Zalogowany użytkownik widzi listę swoich zapisanych notatek i listę swoich materiałów źródłowych, i z każdej z nich otwiera wybrany element. Nie widzi niczego, co należy do innego konta.

Ten plasterek nie jest jednak „tylko listą". Dziś aplikacja **nie ma wejścia** do własnych treści: `NavMenu` oferuje Home / „Zaimportuj materiał" / Wyloguj, `/materials/{id}` jest osiągalne wyłącznie przez przekierowanie po imporcie, a `/notes/{id}` wyłącznie przez przekierowanie po zapisie. Strona notatki i strona materiału linkują do siebie nawzajem wewnątrz wysepki bez drzwi — po opuszczeniu przeglądarki zapisana notatka jest nieosiągalna bez wklejenia adresu z ręki. To ustalenie F6 z przeglądu planu S-01c, jedyne, które tamten plasterek zostawił otwarte i przekazał tutaj wprost (`context/archive/2026-08-17-note-review-save/reviews/plan-review.md:105-115`, `change.md:17`). **Zadaniem S-03 jest wejście do grafu treści, nie samo listowanie.**

Nowe w projekcie są dwie rzeczy: **pierwsze strony na static SSR, które czytają bazę**, oraz **pierwsze typy projekcji** (do dziś każdy odczyt zwraca całe encje).

## Current State Analysis

Co istnieje po S-01c (`note-review-save`, zarchiwizowane 2026-08-17):

- `Data/Entities/Note.cs` — `Title` (200, wymagane), `Content`, `DraftContent`, `PromptVersion`, `Model`, `CreatedAt`, `UpdatedAt`, `SourceMaterialId` z **unikalnym** indeksem (`AppDbContext.cs`, blok `Entity<Note>`), FK do `source_materials` z `NoAction`. Relacja materiał↔notatka jest ściśle 1:1.
- `Data/Entities/SourceMaterial.cs` — `Title` (200), `Content` (nieograniczony `text`), `OriginalFileName` (260), `CreatedAt`, **nieunikalny** indeks na `OwnerId` założony w S-01a wprost „for the list query in S-03" (`context/archive/2026-08-17-markdown-import/plan.md:89`).
- `Data/AppDbContext.cs` — filtr właściciela zakładany automatycznie na każdy typ implementujący `IOwnedByUser`; `OwnerId` jest tokenem współbieżności; `StampOwners` nadpisuje właściciela z zalogowanego użytkownika. **Per-query filtry właściciela są zabronione przez `CLAUDE.md`.**
- `Notes/NoteService.cs` — jedyny precedens serwisu czytającego notatki: bierze `UserScopedDbContextFactory`, zwraca typowany wynik, `GetAsync` / `GetByMaterialAsync` zwracają całą encję `Note`.
- **Materiały nie mają serwisu.** `Import.razor:119`, `Materials/Detail.razor:229` i `Notes/Detail.razor:119` odpytują `db.SourceMaterials` bezpośrednio przez `UserScopedDbContextFactory`.
- `Components/Pages/Home.razor` — **7 linii nietkniętego szablonu**: „Hello, world!". Trasa `/` jest dziś martwa.
- `Components/Layout/NavMenu.razor:11-36` — `AuthorizeView`, trzy wpisy dla zalogowanego. `NavMenu.razor.css:45` definiuje **nieużywaną** klasę ikony `.bi-list-nested-nav-menu`.
- `Program.cs:104-109` — `FallbackPolicy` wymaga uwierzytelnionego użytkownika, więc nowa strona jest chroniona bez atrybutu; strony szczegółów i tak dokładają `@attribute [Authorize]` jawnie.
- `tests/10xNotes.Tests/` — xUnit 2.9.3, gołe `Assert`, SQLite in-memory, **żadnego bUnit**. `DataAccessBoundaryTests` wywala build, gdy jakikolwiek typ w assembly aplikacji bierze `DbContext` **albo `IDbContextFactory<>`**; `deploy.yml` uruchamia suite przed deployem.

Czego brakuje: jakiejkolwiek listy, jakiegokolwiek wejścia do treści z nawigacji, i jakiegokolwiek zapytania, które zwraca mniej niż całą encję.

## Desired End State

Zalogowany użytkownik wchodzi na `/` i widzi **Moje notatki** — listę zapisanych notatek, najnowszy zapis na górze, każdy wiersz z tytułem (link do `/notes/{id}`), datą zapisu i linkiem do materiału źródłowego. Klika „Moje materiały" w nawigacji, ląduje na `/materials` i widzi **listę materiałów**, najnowszy import na górze, każdy wiersz z tytułem (link do `/materials/{id}`), datą importu, nazwą pliku i — gdy notatka istnieje — linkiem do niej. Świeże konto widzi na obu stronach komunikat mówiący, co zrobić dalej, z linkiem prowadzącym tam, gdzie to zrobić.

Drugie konto zalogowane równolegle nie widzi na żadnej z tych list ani jednego wiersza pierwszego konta. Wylogowany użytkownik wchodzący na `/` ląduje na `/login?ReturnUrl=%2F`.

Weryfikacja: `dotnet build` czysty, `dotnet test` zielony (łącznie z nowymi przypadkami izolacji, kolejności i obecności notatki), plus ręczne przejście: puste konto → import → `/materials` → generowanie i zapis → `/` → oba linki krzyżowe.

### Key Discoveries:

- **Żadnej migracji.** Obie tabele mają już wszystko, czego wiersz listy potrzebuje — łącznie z indeksem na `OwnerId`, który S-01a założył właśnie pod to zapytanie. Nie powstaje żadna nowa tabela, więc reguła RLS z `lessons.md` nie ma tu czego dotyczyć. Jeśli implementacja zaczyna pisać migrację, coś poszło nie tak.
- **Static SSR jest dostępne i wystarczające.** `Auth/AuthenticationStateCurrentUserAccessor.cs:24-26` wprost mówi, że warstwa danych zawodzi zamknięcie „on every path, including static SSR", a `Program.cs:117-126` rejestruje `SessionCapAuthenticationStateProvider` również jako `IHostEnvironmentAuthenticationStateProvider` — to ścieżka, którą static SSR wpycha principal. Strony list nie potrzebują obwodu; będą pierwszymi stronami static SSR w projekcie, które czytają bazę.
- **Lista notatek nie potrzebuje joinu.** `NoteValidator.DeriveTitle` bierze tytuł notatki z tytułu materiału (`Materials/Detail.razor:444`), więc te dwa napisy są niemal zawsze identyczne. Wiersz notatki linkuje do `/materials/{SourceMaterialId}` stałą kopią zamiast powtarzać ten sam tytuł dwa razy.
- **Projekcja jest wymogiem, nie optymalizacją.** `SourceMaterial.Content` to nieograniczony `text` z realnym pułapem 128 KB, `Note.Content` i `Note.DraftContent` po 64 KB. Lista dwudziestu materiałów zwracana jako encje to megabajty tekstu przeciągnięte do pamięci po to, żeby wyświetlić tytuł.
- **Brak bUnit jest wiążący.** Cokolwiek ma być udowodnione, musi mieszkać poza komponentem — dokładnie jak `NoteValidator`, `MarkdownImportValidator` i `GenerationQuotaService`.
- **`Index.razor` w dwóch folderach jest legalne.** `Materials/Detail.razor` i `Notes/Detail.razor` już współistnieją — nazwy komponentów są unikalne w obrębie namespace'u, a te się różnią.
- **`.bi-list-nested-nav-menu` już istnieje w CSS** (`NavMenu.razor.css:45`) i nie jest przez nic używana. Nowy wpis nawigacji nie wymaga nowej ikony.

## What We're NOT Doing

- **Wyszukiwania, filtrów i sortowania z UI** — poza MVP (`roadmap.md:164` nazywa to wprost jako ryzyko rozrostu tego plasterka). Kolejność jest ustalona i niesterowalna.
- **Paginacji, „pokaż więcej" i `Virtualize`** — decyzja: pełna lista, najnowsze na górze. PRD ustawia `data_volume: small`, a produkt jest jednoosobowy. Cena jest zapisana niżej w Performance Considerations.
- **Usuwania z listy** — to S-05 (`delete-note`) i S-06 (`delete-source-material`, zablokowane OQ2). Listy są **wyłącznie do czytania**; żadnego przycisku, który zmienia stan.
- **Podglądu treści w wierszu** (excerpt) — wymagałby albo `substring` po stronie SQL, albo wciągnięcia całych kolumn tekstowych; a wycinek surowego Markdownu czyta się źle. Wiersz orientuje tytułem i datą.
- **Linków powrotnych na stronach szczegółów** — świadomie odrzucone. `/notes/{id}` i `/materials/{id}` zostają nietknięte; wyjściem z nich jest przycisk Wstecz przeglądarki i nawigacja.
- **Przekierowania paneli „nie znaleziono"** — oba dalej linkują do `/materials/import`, nie do nowych list. Świadomie odrzucone.
- **Usuwania szablonowego linku „About"** z `MainLayout.razor:10` — świadomie odrzucone, to nie jest sprzątanie tego plasterka.
- **Refaktoru istniejących bezpośrednich zapytań** o materiał w `Import.razor`, `Materials/Detail.razor` i `Notes/Detail.razor` do nowego serwisu. Nowy serwis dostaje jedną metodę — listę. Przeniesienie pozostałych odczytów to zmiana bez powodu w plasterku, który ich nie dotyka.
- **Landing page dla niezalogowanych** — `/` jest pod `FallbackPolicy`, więc anonim leci na `/login`. Produkt nie ma publicznej strony wejściowej i ten plasterek jej nie tworzy.
- **Jakiejkolwiek zmiany w modelu danych, migracjach, promptcie, generowaniu i rejestrze akceptacji.**

## Implementation Approach

Dwie warstwy, w tej kolejności, bo pierwsza jest jedyną, którą da się w tym repo w ogóle przetestować.

**Warstwa zapytań** to dwie metody zwracające `IReadOnlyList<T>` rekordów projekcji: jedna na istniejącym `NoteService`, druga na nowym `SourceMaterialService`. Oba biorą `UserScopedDbContextFactory`, oba nie piszą ani jednego filtru właściciela — globalny filtr `AppDbContext` robi to za nie, i to on jest dowodem prywatności. Projekcje niosą wyłącznie to, co pokazuje wiersz, więc żadna kolumna treści nie opuszcza bazy.

**Warstwa widoku** to dwie strony na static SSR, bez `@rendermode`. Nie ma tu nic interaktywnego: żadnego przycisku, żadnego pola, żadnego strumienia — tylko odczyt i lista linków. Brak obwodu oznacza też, że `OnInitializedAsync` wykonuje się raz, a nie dwa razy jak na stronach szczegółów. Kopia jest polska, daty idą przez `TimeProvider.ToDisplayTime` z kulturą `pl-PL` — tym samym formatem, którego używają obie strony szczegółów.

Wejście domyka podmiana `Home.razor` na listę notatek pod `/` i dwa wpisy w `NavMenu`.

## Critical Implementation Details

**Static SSR, nie interaktywność.** Ani jedna z nowych stron nie dostaje `@rendermode`. To nie jest oszczędność, to poprawność: strona listy nie ma stanu, który miałby żyć, a `@rendermode InteractiveServer` kazałby `OnInitializedAsync` wykonać się dwukrotnie (raz w prerenderze, raz po otwarciu obwodu) — czyli dwa razy odpytać bazę o tę samą listę. `Materials/Detail.razor:215-220` dokumentuje ten koszt jako świadomie zaakceptowany tam, gdzie obwód jest potrzebny do strumieniowania. Tu nie jest.

**Left join nie może rozmnożyć wierszy ani przeciec.** Lista materiałów potrzebuje odpowiedzi „czy istnieje notatka", co jest joinem między dwiema tabelami aplikacji — pierwszym takim zapytaniem w projekcie (relacja jest skonfigurowana bez właściwości nawigacyjnych, bo projekt ich nie ma nigdzie). Dwie rzeczy trzymają to w ryzach i obie są własnością modelu, nie zapytania: globalny filtr właściciela obowiązuje **oba** zbiory encji, więc żadna strona joinu nie może sięgnąć na cudze konto; a unikalny indeks na `Note.SourceMaterialId` gwarantuje najwyżej jedno dopasowanie, więc `DefaultIfEmpty()` nie potrafi zduplikować wiersza materiału.

**Kolejność musi mieć rozstrzygnięcie remisu.** `Note.UpdatedAt` i `SourceMaterial.CreatedAt` mają domyślnie `now()` po stronie Postgresa, a dwa importy w tej samej milisekundzie nie są niemożliwe. Bez drugiego klucza sortowania kolejność takich wierszy jest niezdefiniowana i może się zmieniać między odświeżeniami. Sortowanie kończy się na `Id`.

---

## Phase 1: Warstwa zapytań

### Overview

Dwie testowalne metody listujące i dwa typy projekcji. Żadnego UI, żadnej migracji.

### Changes Required:

#### 1. Projekcja wiersza notatki

**File**: `Notes/NoteListItem.cs`

**Intent**: Nieść dokładnie to, co pokazuje wiersz listy notatek, i ani jednego bajtu treści. Pierwszy typ projekcji w projekcie — warto go opisać jako świadomy precedens, bo do tej pory każdy odczyt zwracał całą encję.

**Contract**: `public sealed record NoteListItem(Guid Id, string Title, DateTimeOffset UpdatedAt, Guid SourceMaterialId);` w namespace `_10xnotes.Notes`. Bez `Content`, bez `DraftContent`, bez `Model` i `PromptVersion` — lista nie ma ich po co czytać.

#### 2. Zapytanie listujące notatki

**File**: `Notes/NoteService.cs`

**Intent**: Zwrócić notatki zalogowanego użytkownika, najnowszy zapis na górze, jako projekcje. Metoda ląduje na istniejącym serwisie, bo to on już jest właścicielem odczytów notatek.

**Contract**: `public async Task<IReadOnlyList<NoteListItem>> ListAsync(CancellationToken cancellationToken = default)`. Kontekst przez `dbContextFactory.CreateAsync`, jak pozostałe metody. **Żadnego filtru właściciela w zapytaniu** — robi to globalny filtr. Sortowanie: `UpdatedAt` malejąco, następnie `Id` (rozstrzygnięcie remisu, patrz Critical Implementation Details). `Select` do `NoteListItem` **przed** materializacją, żeby SQL nie zawierał kolumn treści.

#### 3. Projekcja wiersza materiału

**File**: `SourceMaterials/SourceMaterialListItem.cs`

**Intent**: To samo dla materiału, plus jedna informacja, której sam materiał nie ma: czy istnieje dla niego zapisana notatka i pod jakim id ją otworzyć.

**Contract**: `public sealed record SourceMaterialListItem(Guid Id, string Title, string OriginalFileName, DateTimeOffset CreatedAt, Guid? NoteId);` w namespace `_10xnotes.SourceMaterials`. `NoteId` jest `null`, gdy notatki nie ma — to jedyny nośnik „ma notatkę / nie ma notatki". Tytuł notatki **nie** jest niesiony (patrz Key Discoveries).

#### 4. Serwis materiałów

**File**: `SourceMaterials/SourceMaterialService.cs`

**Intent**: Dać liście materiałów szew poza komponentem, bo bez bUnit to jedyny sposób, żeby cokolwiek z niej udowodnić. Nowy typ, a nie metoda na czymś istniejącym: `SourceMaterials/` ma dziś tylko czysty walidator, a odczyty materiału są rozsypane po stronach.

**Contract**: `public sealed class SourceMaterialService(UserScopedDbContextFactory dbContextFactory)` z jedną metodą `public async Task<IReadOnlyList<SourceMaterialListItem>> ListAsync(CancellationToken cancellationToken = default)`. **Musi brać `UserScopedDbContextFactory`, nigdy `IDbContextFactory<>` ani `DbContext`** — `DataAccessBoundaryTests` wywala build i blokuje deploy. Sortowanie: `CreatedAt` malejąco, następnie `Id`.

Zapytanie jest lewostronnym złączeniem `source_materials` → `notes` po `SourceMaterialId`, zbudowanym bez właściwości nawigacyjnych (projekt nie ma ani jednej), czyli group join z `DefaultIfEmpty()`, i kończy się `Select` do `SourceMaterialListItem`, w którym brak dopasowania daje `NoteId = null`. Nic ponad to nie jest tu potrzebne — warunki bezpieczeństwa tego joinu są własnością modelu i są opisane w Critical Implementation Details.

#### 5. Rejestracja w kontenerze

**File**: `Program.cs`

**Intent**: Udostępnić nowy serwis stronom.

**Contract**: `builder.Services.AddScoped<SourceMaterialService>();` obok istniejących rejestracji `NoteGenerator` / `GenerationQuotaService` / `NoteService`, plus `using _10xnotes.SourceMaterials;`. Scoped, jak wszystkie pozostałe serwisy — kontekst i tak powstaje per operacja.

#### 6. Testy

**File**: `tests/10xNotes.Tests/NoteServiceTests.cs` (nowa sekcja) i nowy `tests/10xNotes.Tests/SourceMaterialServiceTests.cs`

**Intent**: Udowodnić trzy rzeczy, których strona nie potrafi udowodnić: listy pokazują wyłącznie wiersze właściciela, kolejność jest ustalona, a obecność notatki jest raportowana poprawnie w obu wariantach.

**Contract**: Harness jak w `NoteServiceTests` — SQLite in-memory, `EnsureCreated`, dwaj użytkownicy (`UserA` / `UserB`), gołe `Assert`, żadnej biblioteki mockującej. `Id` i znaczniki czasu w danych zasiewanych ręcznie, bo ich defaulty to funkcje Postgresa, których SQLite nie policzy (precedens: `OwnerScopingTests`).

Przypadki:
- `ListAsync` notatek zwraca tylko notatki `UserA`, gdy oba konta mają zapisane notatki.
- `ListAsync` notatek zwraca je od najnowszego `UpdatedAt`; dwie notatki z identycznym `UpdatedAt` wychodzą w stabilnej kolejności po `Id`.
- `ListAsync` notatek dla konta bez notatek zwraca pustą listę, nie `null`.
- `ListAsync` materiałów zwraca tylko materiały `UserA`.
- `ListAsync` materiałów podaje `NoteId` materiału z zapisaną notatką i `null` dla materiału bez notatki — obydwa w jednym wywołaniu, żeby złączenie było sprawdzone naprawdę, a nie na jednorodnym zestawie.
- Notatka `UserB` przy materiale `UserA` **nie** wycieka jako `NoteId` (sytuacja niemożliwa w produkcji, bo zapis stempluje właściciela — ale to jest test tego, że join jest filtrowany po obu stronach, a nie po jednej).
- `ListAsync` materiałów dla konta bez materiałów zwraca pustą listę.

### Success Criteria:

#### Automated Verification:

- Build jest czysty: `dotnet build`
- Cała suite przechodzi: `dotnet test`
- `DataAccessBoundaryTests` przechodzi, czyli nowy serwis nie bierze `DbContext` ani `IDbContextFactory<>`
- Nowe przypadki z §6 przechodzą, łącznie z izolacją właściciela na obu listach i wariantami `NoteId`
- W repo nie powstał żaden nowy plik w `Migrations/`: `git status --porcelain Migrations/` nie zwraca nic

---

## Phase 2: Strony list i wejście do treści

### Overview

Dwie strony na static SSR, podmiana martwej trasy `/` i dwa wpisy w nawigacji — czyli moment, w którym wysepka dostaje drzwi.

### Changes Required:

#### 1. Lista notatek pod `/`

**File**: `Components/Pages/Notes/Index.razor` (nowy)

**Intent**: Front door produktu: to, po co użytkownik wraca. Wchodzi na `/` i widzi swoje zapisane notatki, najnowszą na górze.

**Contract**: `@page "/"`, `@attribute [Authorize]`, **bez `@rendermode`** (static SSR — patrz Critical Implementation Details). Wstrzykuje `NoteService` i `TimeProvider`; nie wstrzykuje żadnego kontekstu ani fabryki kontekstu. `OnInitializedAsync` woła `NoteService.ListAsync()`.

`<PageTitle>` i `<h1>`: „Moje notatki". Lista jako Bootstrapowa `list-group` (Bootstrap 5.3 jest w `wwwroot/`), jeden wiersz na notatkę: tytuł jako link do `/notes/{Id}`, pod nim „Zapisano @TimeProvider.ToDisplayTime(item.UpdatedAt).ToString("d MMMM yyyy, HH:mm", PolishCulture)" oraz link „Materiał źródłowy" do `/materials/{SourceMaterialId}`. `PolishCulture` jak na stronach szczegółów: `private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl-PL");` — `ToDisplayTime`, nie `ToLocalTime`, bo „local" na serwerze to UTC kontenera.

Stan pusty (lista zero elementów): komunikat mówiący, co zrobić dalej, plus link do `/materials` — np. „Nie masz jeszcze żadnej zapisanej notatki. Wybierz materiał i wygeneruj z niego notatkę." Bez zgadywania, czy użytkownik ma materiały: ta strona ich nie czyta, a link do listy materiałów odpowiada na oba przypadki.

Tytuły notatek pochodzą od użytkownika, więc renderują się jako tekst przez zwykłą interpolację — **żadnego `MarkupString`**. Jedyne miejsce w projekcie, gdzie treść staje się markupem, to podgląd w `NoteEditor` przez `NoteMarkdown`, i tak zostaje.

#### 2. Lista materiałów pod `/materials`

**File**: `Components/Pages/Materials/Index.razor` (nowy)

**Intent**: Druga połowa FR-007: materiały jako osobna lista najwyższego poziomu, z której widać, które mają już notatkę.

**Contract**: `@page "/materials"`, `@attribute [Authorize]`, **bez `@rendermode`**. Wstrzykuje `SourceMaterialService` i `TimeProvider`. Struktura jak w §1.

`<PageTitle>` i `<h1>`: „Moje materiały". Wiersz: tytuł jako link do `/materials/{Id}`, pod nim „Zaimportowano \<data\> z pliku `<code>@item.OriginalFileName</code>`" tym samym formatem daty co strona szczegółów materiału, oraz — gdy `NoteId` nie jest `null` — link „Otwórz notatkę" do `/notes/{NoteId}`; w przeciwnym razie neutralna informacja, że notatki jeszcze nie ma.

Stan pusty: „Nie masz jeszcze żadnego materiału." plus link do `/materials/import`.

#### 3. Usunięcie szablonowej strony startowej

**File**: `Components/Pages/Home.razor` (usunięcie)

**Intent**: `@page "/"` może istnieć tylko raz. Poza tym to ostatnia strona nietknięta od szablonu — 7 linii „Hello, world!".

**Contract**: Plik znika. Nic go nie referuje: jedyne wystąpienie słowa „Home" poza nim samym to etykieta linku w `NavMenu`, którą i tak podmienia §4.

#### 4. Wejście z nawigacji

**File**: `Components/Layout/NavMenu.razor`

**Intent**: Bez tego listy istnieją i są tak samo nieosiągalne jak strony, które indeksują. To zmiana, która zamyka F6.

**Contract**: W bloku `<Authorized>`: pierwszy wpis przestaje być „Home" i zostaje „Moje notatki" (dalej `href=""` z `Match="NavLinkMatch.All"`, ikona `bi-house-door-fill-nav-menu` bez zmian). Nowy wpis „Moje materiały" na `href="materials"` z ikoną `bi-list-nested-nav-menu` — klasa już istnieje w `NavMenu.razor.css:45` i nie jest przez nic używana, więc CSS nie wymaga zmian. Kolejność: Moje notatki → Moje materiały → Zaimportuj materiał → nazwa użytkownika → Wyloguj się.

Nowy wpis dostaje `Match="NavLinkMatch.All"`. Domyślne dopasowanie po prefiksie podświetlałoby „Moje materiały" również na `/materials/import` i `/materials/{id}`, czyli razem z wpisem importu — dwa aktywne wpisy naraz.

### Success Criteria:

#### Automated Verification:

- Build jest czysty: `dotnet build`
- Cała suite przechodzi: `dotnet test`
- `Components/Pages/Home.razor` nie istnieje
- Żadna z nowych stron nie deklaruje `@rendermode`: `grep -L "@rendermode" Components/Pages/Notes/Index.razor Components/Pages/Materials/Index.razor` wypisuje oba pliki
- Żadna z nowych stron nie wstrzykuje kontekstu ani fabryki kontekstu — pilnuje tego `DataAccessBoundaryTests`, który skanuje całe assembly aplikacji, komponenty Razor włącznie

#### Manual Verification:

- Świeże konto: `/` pokazuje stan pusty notatek z linkiem do materiałów; `/materials` pokazuje stan pusty materiałów z linkiem do importu
- Po imporcie: materiał jest na górze `/materials`, bez linku do notatki; `/` nadal pusty
- Po wygenerowaniu i zapisaniu notatki: notatka jest na `/` z datą zapisu, a wiersz jej materiału na `/materials` oferuje link do notatki
- Linki krzyżowe w obie strony otwierają właściwe strony szczegółów
- Ponowny zapis notatki z `/notes/{id}` przesuwa ją na górę listy notatek
- Nawigacja pokazuje „Moje notatki" i „Moje materiały"; na `/materials` podświetla się wyłącznie „Moje materiały", na `/materials/import` wyłącznie „Zaimportuj materiał"
- Drugie konto zalogowane równolegle nie widzi na żadnej z list ani jednego wiersza pierwszego konta
- Wylogowany użytkownik wchodzący na `/` ląduje na `/login?ReturnUrl=%2F` i po zalogowaniu wraca na `/`
- Materiał zaimportowany, gdy jego nazwa pliku i tytuł zawierają polskie znaki, wyświetla je poprawnie w obu listach

**Implementation Note**: Po zakończeniu tej fazy i przejściu weryfikacji automatycznej zatrzymaj się i poczekaj na potwierdzenie od człowieka, że ręczne testy wypadły dobrze. Bloki faz używają zwykłych punktów — odpowiadające im checkboxy żyją w sekcji `## Progress` na końcu planu.

---

## Testing Strategy

### Unit Tests:

- Izolacja właściciela na obu listach — to wykonywalny dowód gwardraila prywatności z PRD dla tego plasterka i najważniejszy test tej zmiany.
- Kolejność (malejąco po dacie) i stabilność remisu po `Id`.
- Obecność notatki: `NoteId` wypełnione i `null` w jednym wywołaniu, na mieszanym zestawie danych.
- Puste konto zwraca pustą listę, nie `null`.

### Integration Tests:

Brak — projekt nie ma harnessu integracyjnego, a jedyne rzeczy specyficzne dla Postgresa (FK do `auth.users`, `NO ACTION`, RLS) nie są przez ten plasterek dotykane. Złączenie jest sprawdzane na SQLite; to ta sama translacja LINQ i to samo `LEFT JOIN`.

### Manual Testing Steps:

1. Zaloguj się na świeżym koncie, wejdź na `/` i na `/materials` — sprawdź oba stany puste i ich linki.
2. Zaimportuj plik `.md`, wróć na `/materials` — materiał jest na górze, bez notatki.
3. Wygeneruj notatkę i zapisz ją. Wejdź na `/` — notatka jest na liście z datą zapisu.
4. Wróć na `/materials` — wiersz materiału ma teraz link do notatki. Kliknij go.
5. Popraw notatkę i zapisz ponownie; wróć na `/` — została na górze z nową godziną.
6. Zaimportuj drugi materiał i sprawdź kolejność obu list.
7. W drugiej przeglądarce zaloguj się na drugie konto — obie listy są puste. Wklej z ręki adres notatki pierwszego konta: „Nie znaleziono notatki".
8. Wyloguj się i wejdź na `/` — przekierowanie na logowanie, po zalogowaniu powrót na `/`.

## Performance Considerations

Obie listy to jedno zapytanie na wejście na stronę, bez `N+1`: obecność notatki przychodzi jednym złączeniem, nie zapytaniem per wiersz. Projekcja jest tym, co utrzymuje transfer w kilobajtach — bez niej lista dwudziestu materiałów przeciąga do pamięci ich pełną treść. Oba zapytania filtrują po `owner_id`, dla którego obie tabele mają indeks.

Zapytania są **nieograniczone** — świadomie, wobec `data_volume: small` w PRD i produktu jednoosobowego. Cena jest realna i nazwana wprost: konto z tysiącami materiałów wyrenderuje jedną bardzo długą stronę i przeczyta wszystkie wiersze. Progiem, po którym to przestaje być akceptowalne, jest moment, w którym którakolwiek lista przestaje mieścić się na ekranie w rozsądnym przewijaniu; wtedy wraca decyzja o paginacji albo `Virtualize` (klasa jest już zaimportowana w `_Imports.razor:9`). Static SSR łagodzi to o tyle, że nie ma tu obwodu, przez który ta lista musiałaby iść diffem.

## Migration Notes

Brak migracji i brak zmian w schemacie. Żadna nowa tabela nie powstaje, więc reguła „RLS w tej samej migracji" z `lessons.md` nie ma tu zastosowania. Zmiana jest w pełni odwracalna rollbackiem obrazu — poza jednym szczegółem: `/` przestaje być stroną szablonu, a rollback ją przywraca.

## References

- Roadmap: `context/foundation/roadmap.md:154-166` (S-03), oraz `:40` w tabeli indeksu
- PRD: `context/foundation/prd.md` — FR-007, NFR prywatności, `target_scale.data_volume: small`
- Ustalenie przekazane tutaj: `context/archive/2026-08-17-note-review-save/reviews/plan-review.md:105-115` (F6) i `context/archive/2026-08-17-note-review-save/change.md:17`
- Indeks założony pod to zapytanie: `context/archive/2026-08-17-markdown-import/plan.md:89`
- Precedens serwisu z `UserScopedDbContextFactory`: `Notes/NoteService.cs:26`
- Precedens harnessu testowego: `tests/10xNotes.Tests/NoteServiceTests.cs:23`, `tests/10xNotes.Tests/OwnerScopingTests.cs`
- Formatowanie daty i kultura: `Components/Pages/Materials/Detail.razor:46`, `Components/Pages/Notes/Detail.razor:39`
- Granica dostępu do danych: `tests/10xNotes.Tests/DataAccessBoundaryTests.cs`, `CLAUDE.md` §Data & persistence rules

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Warstwa zapytań

#### Automated

- [x] 1.1 Build jest czysty: `dotnet build` — 15e624a
- [x] 1.2 Cała suite przechodzi: `dotnet test` — 15e624a
- [x] 1.3 `DataAccessBoundaryTests` przechodzi, czyli nowy serwis nie bierze `DbContext` ani `IDbContextFactory<>` — 15e624a
- [x] 1.4 Nowe przypadki z §6 przechodzą, łącznie z izolacją właściciela na obu listach i wariantami `NoteId` — 15e624a
- [x] 1.5 W repo nie powstał żaden nowy plik w `Migrations/` — 15e624a

### Phase 2: Strony list i wejście do treści

#### Automated

- [x] 2.1 Build jest czysty: `dotnet build`
- [x] 2.2 Cała suite przechodzi: `dotnet test`
- [x] 2.3 `Components/Pages/Home.razor` nie istnieje
- [x] 2.4 Żadna z nowych stron nie deklaruje `@rendermode`
- [x] 2.5 Żadna z nowych stron nie wstrzykuje kontekstu ani fabryki kontekstu

#### Manual

- [ ] 2.6 Świeże konto widzi oba stany puste z ich linkami
- [ ] 2.7 Po imporcie materiał jest na górze `/materials`, bez linku do notatki; `/` nadal pusty
- [ ] 2.8 Po zapisie notatka jest na `/` z datą, a wiersz materiału oferuje link do notatki
- [ ] 2.9 Linki krzyżowe w obie strony otwierają właściwe strony szczegółów
- [ ] 2.10 Ponowny zapis notatki przesuwa ją na górę listy notatek
- [ ] 2.11 Nawigacja pokazuje oba wpisy i podświetla dokładnie jeden na `/materials` oraz na `/materials/import`
- [ ] 2.12 Drugie konto nie widzi na listach ani jednego wiersza pierwszego konta
- [ ] 2.13 Wylogowany na `/` ląduje na `/login?ReturnUrl=%2F` i po zalogowaniu wraca na `/`
- [ ] 2.14 Polskie znaki w tytule i nazwie pliku wyświetlają się poprawnie w obu listach
