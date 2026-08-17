# Przegląd, edycja i zapis notatki (S-01c) — Implementation Plan

## Overview

Użytkownik poprawia wygenerowaną notatkę i zapisuje ją. Zapis wiąże notatkę z kontem i **liczy się jako akceptacja**; porzucenie bez zapisu nie pozostawia jej na koncie; materiał źródłowy zostaje nietknięty. Ten plasterek domyka gwiazdę przewodnią — dopiero po nim da się w ogóle mierzyć kryterium 75% akceptacji z PRD.

Trzy rzeczy pojawiają się tu po raz pierwszy w projekcie: **encja notatki** z relacją do innej tabeli aplikacji, **renderowanie Markdownu** (a więc pierwsza ścieżka, w której treść od modelu staje się HTML-em), oraz **rejestr akceptacji**, który zamienia „75% notatek jest akceptowanych" z deklaracji w liczbę.

## Current State Analysis

Co istnieje po S-01b (`ai-note-generation`, zarchiwizowane 2026-08-17):

- `Components/Pages/Materials/Detail.razor` (275 linii) — dwukolumnowy workspace, `@rendermode InteractiveServer`. Notatka żyje wyłącznie w polu `private string note = string.Empty;` (`Detail.razor:105`) i ginie przy odświeżeniu. Panel notatki renderuje `<pre class="note-content">` (`:71-76`) z komentarzem wprost mówiącym, że renderowanie Markdownu należy do S-01c.
- `Generation/NoteGenerator.cs` — `IAsyncEnumerable<GenerationChunk>`, porażka jako element strumienia, nie wyjątek. Cała wiedza o dostawcy kończy się tutaj.
- `Generation/NotePrompt.cs:32` — `public const string Version = "v2";` z uwagą „Not persisted yet; the note is ephemeral until S-01c". Haczyk na zapis wersji promptu jest już wycięty.
- `Data/Entities/SourceMaterial.cs` — jedyna encja domenowa poza `Profile` i `GenerationQuota`. `Title` 200 znaków wymagany, `Content` nieograniczony `text`, `CreatedAt`. **Brak `UpdatedAt` gdziekolwiek w projekcie.**
- `Data/AppDbContext.cs:93-126` — filtr właściciela zakładany automatycznie na każdy typ implementujący `IOwnedByUser`; `OwnerId` jest tokenem współbieżności z `PropertySaveBehavior.Throw`; `StampOwners` (`:190-228`) nadpisuje `OwnerId` z zalogowanego użytkownika i rzuca, gdy nikogo nie ma.
- `Generation/GenerationQuotaService.cs` — jedyny precedens serwisu dotykającego bazy: bierze `UserScopedDbContextFactory`, zwraca typowany wynik, łapie `DbUpdateException` na wyścigu unikalnego indeksu i ponawia.
- `SourceMaterials/MarkdownImportValidator.cs` — precedens czystego szwu: statyczna walidacja bez I/O, mapowana na polską kopię dopiero w `Import.razor:170`.
- `tests/10xNotes.Tests/` — xUnit 2.9.3, gołe `Assert`, **żadnej biblioteki mockującej ani asercyjnej**, ręcznie pisane atrapy jako zagnieżdżone `private sealed class`. SQLite in-memory (`Microsoft.EntityFrameworkCore.Sqlite.Core` + `SQLitePCLRaw.bundle_sqlite3`), nigdy `InMemory`, nigdy prawdziwy Postgres. **Brak bUnit — komponentów Razor nie da się testować w ogóle.**

Czego brakuje: encji notatki, jakiegokolwiek zapisu notatki, edytora, renderowania Markdownu, i jakiegokolwiek śladu pozwalającego policzyć akceptacje.

Dwie rzeczy odziedziczone wprost z przeglądu implementacji S-01b:

- **F9 (SKIPPED, przekazane tutaj)**: prompt prosi o „nagłówki i punkty w Markdownie", a panel pokazuje `<pre>`, więc użytkownik czyta dosłowne `##` i `-`. S-01c miał to rozstrzygnąć — rozstrzyga.
- **F1 i F3 (CRITICAL / WARNING, naprawione)**: ta strona ma udokumentowaną historię wyścigów — zgubiona aktualizacja w liczniku limitu i okno re-entrancy przy podwójnym kliknięciu, które osierocało `CancellationTokenSource`. Dokładanie edytowalnego bufora na bufor strumieniowy wchodzi w ten sam teren.

## Desired End State

Zalogowany użytkownik otwiera `/materials/{id}`, klika **„Generuj notatkę"** i patrzy, jak notatka przyrasta w prawej kolumnie — panel jest w tym czasie tylko do odczytu. Po zakończeniu strumienia panel zamienia się w **edytor**: pole tytułu (wypełnione tytułem materiału, poprawialne), textarea ze źródłem Markdown i przełącznik **Podgląd**, który pokazuje notatkę wyrenderowaną. Użytkownik poprawia i klika **„Zapisz notatkę"**. Jeśli dla tego materiału notatka już istnieje, aplikacja pyta o potwierdzenie zastąpienia. Po zapisie ląduje na `/notes/{id}` — znowu dwie kolumny: materiał po lewej, zapisana notatka po prawej, dalej edytowalna i zapisywalna w miejscu.

Porzucenie bez zapisu nie zostawia niczego na koncie. Ponowne generowanie nad niezapisanymi zmianami pyta o potwierdzenie. Materiał źródłowy jest nietknięty w każdym z tych przepływów.

Weryfikacja: `dotnet build` i `dotnet test` przechodzą, migracja stosuje się czysto, a ręcznie — pełna pętla import → generowanie → poprawka → zapis → `/notes/{id}` → poprawka → ponowny zapis.

### Key Discoveries:

- **`Markdig.DisableHtml()` nie jest sanitizerem.** Dokumentacja Markdiga mówi to wprost: wyłączenie parsowania HTML usuwa wstrzyknięcie surowego HTML-a, ale „does not inherently make rendering untrusted Markdown safe… consider filtering or rewriting URLs". Po `DisableHtml()` jedyne atrybuty pod kontrolą treści to `href`/`src`/`title`, więc **allowlista schematów URL domyka pozostałą powierzchnię** — i obie rzeczy mieszczą się w czystej, testowalnej funkcji.
- **Wersja Markdiga: 1.3.2** (sprawdzone w nuget.org 2026-08-17, właściciel `xoofx`, 73M pobrań).
- **FK `notes → source_materials` potrafi po cichu rozstrzygnąć OQ2.** Domyślne zachowanie EF dla wymaganego FK to `Cascade`, co ustawiłoby odpowiedź na pytanie blokujące S-06 („kaskada czy osierocenie") bez żadnej decyzji. Szczegóły w Critical Implementation Details — wybór to `NoAction`, nie `Cascade` i nie `Restrict`.
- **Brak precedensu relacji między dwiema tabelami aplikacji.** Wszystkie FK w repo to ręczny SQL do `auth.users` (`fk_<tabela>_owner_id_auth_users`). To pierwszy FK, który EF może wygenerować sam — ale w projekcie **nie ma ani jednej właściwości nawigacyjnej**, więc relację konfigurujemy bez nich (`HasOne<SourceMaterial>().WithMany()`).
- **Brak `UpdatedAt` w projekcie.** Ponowny zapis notatki go wymaga; to pierwsza taka kolumna i warto ją nazwać jako świadomy precedens.
- **Brak bUnit jest wiążący.** Cokolwiek ma być udowodnione, musi mieszkać poza komponentem — dokładnie tak, jak `MarkdownImportValidator` i `GenerationQuotaService`.
- **RLS jest obowiązkowe dla obu nowych tabel** (`lessons.md`), wzorem `Migrations/20260817174948_AddGenerationQuota.cs:52-58`.
- **`DataAccessBoundaryTests` obejmie `NoteService` i obie strony** — skan idzie po całym assembly aplikacji, łącznie z komponentami Razor (`@inject` kompiluje się do właściwości `[Inject]`).

## What We're NOT Doing

- **Wielu notatek na jeden materiał** — decyzja: ściśle 1:1, zapis zastępuje. Wersjonowanie i historia notatek są poza MVP (`roadmap.md:139`).
- **Listy notatek i materiałów** — to S-03 (`browse-notes-and-sources`). `/notes/{id}` jest osiągalne przez zapis i przez link ze strony materiału, nie przez indeks.
- **Usuwania notatki** — to S-05 (`delete-note`).
- **Edycji materiału źródłowego** — to S-04, zablokowane OQ1.
- **Rozstrzygania OQ2 (kaskada przy usuwaniu źródła)** — FK jest celowo ustawiony tak, żeby nie przesądzać odpowiedzi.
- **Wklejania tekstu jako źródła** — S-02.
- **Ochrony niezapisanych zmian przy nawigacji** — decyzja: chronimy tylko ponowne generowanie. Wyjście ze strony gubi zmiany, jak dziś. Bez `NavigationLock`.
- **Autozapisu wersji roboczej** — nic nie trafia na konto, dopóki użytkownik nie kliknie zapisu; to wprost model „zapis = akceptacja".
- **UI do odczytu rejestru akceptacji** — rejestr jest zapisywany, nie wyświetlany. Odczyt to zapytanie SQL prowadzącego produkt.
- **Sanitizera HTML jako osobnego pakietu** — uzasadnienie i ryzyko szczątkowe w Critical Implementation Details oraz w Open Risks.

## Implementation Approach

Cztery fazy, każda samodzielnie weryfikowalna, w kolejności rosnących zależności:

1. **Szew notatki (czysty)** — walidacja, wyprowadzenie tytułu i renderowanie Markdownu jako statyczne funkcje bez I/O. Żadnej bazy, żadnego UI, pełne pokrycie testami.
2. **Trwałość notatki** — dwie encje, jedna migracja, jeden serwis. Żadnego UI.
3. **Edytor i zapis na stronie materiału** — współdzielony komponent edytora + domknięcie przepływu na `/materials/{id}`.
4. **Strona notatki** — `/notes/{id}` z ponowną edycją i zapisem w miejscu.

Kolejność jest wymuszona dwukrotnie: Faza 3 konsumuje szew z Fazy 1 i serwis z Fazy 2, a Faza 4 konsumuje komponent edytora, który powstaje w Fazie 3. Podział odpowiedzialności kopiuje `MarkdownImportValidator` → `Import.razor`: serwis i szew zwracają typowane wyniki, strona mapuje je na polską kopię. To jedyne, co czyni ten plasterek testowalnym bez bUnit.

## Critical Implementation Details

**Timing & lifecycle — FK do `source_materials` musi być `NO ACTION`, nie `Cascade` i nie `Restrict`.** Trzy zachowania, trzy różne konsekwencje:

- `Cascade` (domyślne EF dla wymaganego FK) po cichu odpowiada na OQ2 — pytanie, które **blokuje S-06** — ustawiając „usunięcie materiału kasuje notatkę". Decyzja produktowa przemycona w domyślnej wartości frameworka.
- `Restrict` w Postgresie jest sprawdzany **natychmiast** i nie da się go odroczyć. Usunięcie konta kaskaduje z `auth.users` jednocześnie do `source_materials` i `notes`; `RESTRICT` wystrzeliłby w trakcie tej samej instrukcji i **usunięcie konta by się wywaliło**.
- `NO ACTION` odracza sprawdzenie do końca instrukcji, więc kaskada z `auth.users` usuwa oba wiersze i przechodzi, a gołe `DELETE FROM source_materials` dalej się odbija. To jedyne zachowanie, które zostawia OQ2 otwarte i nie psuje usuwania konta.

W EF: `DeleteBehavior.NoAction`. Ponieważ dziś nie ma żadnego UI usuwającego materiał, różnica jest niewidoczna w działaniu — i właśnie dlatego łatwo ją przegapić, a S-06 odziedziczyłby cudzą decyzję.

**State sequencing — dokładnie jeden właściciel bufora notatki w danym momencie.** Panel jest tylko do odczytu w trakcie strumienia i staje się edytowalny dopiero po jego zakończeniu. To nie jest preferencja UX: strumień pisze do bufora z dławionym repaintem co ~100 ms (`Detail.razor:98`), więc gdyby textarea była żywa, wpisywany tekst i przychodzące tokeny biłyby się o to samo pole w sposób niedeterministyczny. Ta strona ma już udokumentowaną historię obu klas błędu (F1 — zgubiona aktualizacja, F3 — okno re-entrancy), więc nowe pole edycji przejmuje treść **raz**, w momencie przejścia „strumień zakończony", i od tej chwili strumień jej nie dotyka.

**Rejestr akceptacji — mianownik i licznik muszą mierzyć to samo zdarzenie.** Zdarzenie `Generated` zapisujemy **wyłącznie po pomyślnym zakończeniu** strumienia: porażka, timeout i anulowanie nie dają notatki, którą ktokolwiek mógłby zaakceptować, więc w mianowniku byłyby szumem obniżającym wskaźnik za coś, co nie jest jakością modelu. Zdarzenie `Saved` zapisujemy **wyłącznie przy akceptacji świeżego wygenerowania**, nigdy przy ponownym zapisie już zapisanej notatki z `/notes/{id}` — inaczej poprawienie literówki podbija licznik przy niezmienionym mianowniku i wskaźnik potrafi przekroczyć 100%. Stąd dwie różne metody serwisu (`AcceptAsync` / `UpdateAsync`), a nie jedna z flagą.

**Debug & observability — bezpieczeństwo renderowania jest testowalne, więc musi być przetestowane.** `DisableHtml()` zdejmuje surowy HTML, ale dokumentacja Markdiga wprost odsyła do filtrowania URL-i. Po wyłączeniu HTML-a jedyne atrybuty pod kontrolą treści to `href`, `src` i `title`, więc allowlista schematów (`http`, `https`, `mailto`; wszystko inne neutralizowane) domyka resztę. Obie własności — brak surowego HTML na wyjściu i zneutralizowany `javascript:` — są zwykłymi asercjami na stringu i należą do testów szwu, nie do ręcznego przeklikania.

## Phase 1: Szew notatki (czysty)

### Overview

Walidacja, wyprowadzenie tytułu i renderowanie Markdownu jako statyczne funkcje bez I/O, w pełni pokryte testami. Nic w bazie, nic w UI.

### Changes Required:

#### 1. Pakiet

**File**: `10xnotes.csproj`

**Intent**: Wprowadzić parser Markdownu, żeby notatka mogła być pokazana jako struktura, a nie jako dosłowne `##` — to domknięcie odziedziczonego ustalenia F9.

**Contract**: `<PackageReference Include="Markdig" Version="1.3.2" />` w istniejącym `ItemGroup`. Bez `Markdig.Signed` i bez osobnego sanitizera — uzasadnienie w Critical Implementation Details.

#### 2. Walidacja notatki

**File**: `Notes/NoteValidator.cs` (nowy)

**Intent**: Nazwać każdy powód, dla którego notatki nie da się zapisać, i wyprowadzić tytuł startowy z materiału — dokładnie tak, jak `MarkdownImportValidator` robi to dla importu.

**Contract**: `static class NoteValidator` z `MaxTitleLength = 200` (zgodnie z `SourceMaterial.Title`) i `MaxContentLength = 64 * 1024`. Metoda walidująca przyjmuje tytuł i treść, zwraca typowany wynik z `NoteValidationFailure { TitleEmpty, TitleTooLong, ContentEmpty, ContentTooLong }`. Metoda `DeriveTitle(string materialTitle)` zwraca tytuł startowy, przycięty do limitu.

Limit treści jest niższy niż 128 KB importu celowo: notatka jest streszczeniem materiału, a pole jest edytowalne przez użytkownika, więc potrzebuje własnej górnej granicy. W bazie kolumna zostaje nieograniczonym `text`, wzorem `SourceMaterial.Content` — granica jest w kodzie aplikacji.

#### 3. Renderowanie Markdownu

**File**: `Notes/NoteMarkdown.cs` (nowy)

**Intent**: Zamienić Markdown notatki na HTML tak, żeby ani treść od modelu, ani treść wpisana przez użytkownika nie mogła wnieść wykonywalnego znacznika ani adresu.

**Contract**: `static class NoteMarkdown` z jedną metodą `ToHtml(string markdown)` zwracającą string. Współdzielony statyczny `MarkdownPipeline` zbudowany raz (`MarkdownPipelineBuilder` jest budowniczym; zbudowany `MarkdownPipeline` jest niezmienny i bezpieczny wątkowo — to zalecany sposób użycia).

Dwie warstwy, obie wymagane:

1. `.DisableHtml()` na budowniczym — usuwa parser bloków HTML i parsowanie HTML-a inline, więc `<script>` w treści wychodzi jako tekst, nie jako znacznik.
2. Allowlista schematów URL — dokument jest parsowany, przechodzimy po węzłach linków i obrazów, a adres, którego schemat nie jest `http`, `https` ani `mailto`, jest neutralizowany. Dopiero potem renderujemy. To jest ta pozostała powierzchnia, o której mówi dokumentacja Markdiga; `DisableHtml()` sam jej nie zamyka.

Adresy względne i kotwice (`#sekcja`) przechodzą — nie mają schematu, więc nie mogą wykonać kodu.

#### 4. Testy szwu

**File**: `tests/10xNotes.Tests/NoteValidatorTests.cs` (nowy), `tests/10xNotes.Tests/NoteMarkdownTests.cs` (nowy)

**Intent**: Przypiąć limity i — ważniejsze — udowodnić obie własności bezpieczeństwa renderowania na stringu, bez przeglądarki.

**Contract**: Wzorem `MarkdownImportValidatorTests`: gołe `Assert`, `[Theory]`/`[InlineData]` dla granic, nazwy metod pełnymi zdaniami w `Snake_case`.

`NoteValidatorTests`: pusty tytuł, tytuł na granicy i ponad, pusta treść (w tym sama biała spacja), treść na granicy i ponad, wyprowadzenie tytułu z materiału wraz z przycięciem.

`NoteMarkdownTests`: nagłówki i punkty stają się `<h2>` / `<li>`; `<script>alert(1)</script>` w treści **nie** pojawia się na wyjściu jako znacznik; `<img onerror=...>` również nie; `[x](javascript:alert(1))` nie zostawia `javascript:` w wyjściu; `[x](https://example.com)` przechodzi nietknięty; link względny i kotwica przechodzą; `mailto:` przechodzi.

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `NoteMarkdownTests` dowodzi, że surowy HTML nie wychodzi na wyjściu i że `javascript:` w adresie jest zneutralizowany
- `NoteValidatorTests` pokrywa każdą wartość `NoteValidationFailure`
- `DataAccessBoundaryTests` nadal przechodzi

#### Manual Verification:

- Brak — faza jest czysta i w całości pokryta testami automatycznymi

---

## Phase 2: Trwałość notatki i rejestr akceptacji

### Overview

Dwie encje, jedna migracja, jeden serwis. Notatka jest zapisywalna i nadpisywalna, a każde wygenerowanie i każda akceptacja zostawiają ślad w rejestrze.

### Changes Required:

#### 1. Encja notatki

**File**: `Data/Entities/Note.cs` (nowy)

**Intent**: Zapisać zaakceptowaną notatkę wraz z tym, co pozwoli później ocenić, ile pracy człowiek musiał w nią włożyć.

**Contract**: `sealed class Note : IOwnedByUser` — `Guid Id`, `Guid OwnerId`, `Guid SourceMaterialId`, `string Title`, `string Content` (tekst zapisany przez użytkownika), `string DraftContent` (oryginalny szkic modelu, sprzed edycji), `string PromptVersion`, `string Model`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`.

`DraftContent` obok `Content` jest tym, co zamienia „75%" z odpowiedzi tak/nie w mierzalne „ile trzeba było poprawić". `PromptVersion` konsumuje `NotePrompt.Version`, który istnieje od S-01b właśnie po to.

`UpdatedAt` to **pierwsza taka kolumna w projekcie** — pozostałe encje mają wyłącznie `CreatedAt`. Wymaga jej ponowny zapis z `/notes/{id}`; warto to odnotować w komentarzu encji jako świadomy precedens, a nie przeoczenie.

#### 2. Encja rejestru

**File**: `Data/Entities/NoteEvent.cs` (nowy), `Data/Entities/NoteEventKind.cs` (nowy)

**Intent**: Prowadzić dopisywalny wyłącznie rejestr, który przeżywa nadpisanie notatki — bo przy modelu 1:1 sama tabela notatek nie potrafi powiedzieć, ile notatek kiedykolwiek zaakceptowano.

**Contract**: `enum NoteEventKind { Generated, Saved }`. `sealed class NoteEvent : IOwnedByUser` — `Guid Id`, `Guid OwnerId`, `Guid SourceMaterialId`, `NoteEventKind Kind`, `string PromptVersion`, `string Model`, `int DraftLength`, `int SavedLength`, `DateTimeOffset OccurredAt`.

`SourceMaterialId` jest **zwykłym `Guid`, bez FK** — rejestr ma przeżyć usunięcie materiału, inaczej przestaje być dopisywalny wyłącznie i miara akceptacji kasuje się razem z treścią. To także trzyma rejestr z dala od OQ2.

Wskaźnik akceptacji to `count(Saved) / count(Generated)`. `SavedLength` przy `Generated` wynosi zero.

#### 3. Rejestracja w kontekście

**File**: `Data/AppDbContext.cs`

**Intent**: Dodać oba `DbSet`-y i konfigurację encji obok istniejących; filtr właściciela dołoży się sam przez `IOwnedByUser`.

**Contract**: `DbSet<Note> Notes` i `DbSet<NoteEvent> NoteEvents`. Blok `modelBuilder.Entity<Note>`: klucz na `Id`, `gen_random_uuid()` na `Id` i `now()` na `CreatedAt` (wzorem `SourceMaterial` — te wiersze wstawia strona, nie serwis trzymający własny zegar), `Title` wymagany `HasMaxLength(200)`, `Content` i `DraftContent` wymagane bez limitu, `PromptVersion` i `Model` wymagane z rozsądnymi limitami, **unikalny** indeks na `SourceMaterialId` — to on egzekwuje 1:1.

Relacja bez właściwości nawigacyjnych, bo w projekcie nie ma ani jednej: `HasOne<SourceMaterial>().WithMany().HasForeignKey(n => n.SourceMaterialId).OnDelete(DeleteBehavior.NoAction)`. Wybór `NoAction` jest wyjaśniony w Critical Implementation Details i **musi** trafić do komentarza w tym miejscu — inaczej pierwszy czytelnik „poprawi" go na `Cascade`.

Blok `modelBuilder.Entity<NoteEvent>`: klucz na `Id`, domyślne wartości bazodanowe jak wyżej, indeks na `(OwnerId, OccurredAt)` pod zapytania raportowe. Bez FK do `source_materials`.

#### 4. Migracja

**File**: `Migrations/<timestamp>_AddNoteAndNoteEvent.cs` (generowany)

**Intent**: Utworzyć obie tabele z tymi samymi dwiema rzeczami, których EF nie wyraża, plus jedną, którą wyraża, ale źle by ją domyślił.

**Contract**: `dotnet ef migrations add AddNoteAndNoteEvent`, następnie ręcznie dopisać blok `migrationBuilder.Sql` z FK `owner_id → auth.users(id) ON DELETE CASCADE` dla **obu** tabel oraz `ENABLE ROW LEVEL SECURITY` (bez polityk) dla **obu**. `Down` zdejmuje oba FK przed `DropTable`. Wzorzec skopiować dosłownie z `Migrations/20260817174948_AddGenerationQuota.cs:52-58`.

FK `notes.source_material_id → source_materials(id)` generuje EF; zweryfikować w wygenerowanym pliku, że wyszedł jako `ON DELETE NO ACTION`, i **nie** poprawiać go na `RESTRICT` ani `CASCADE`.

#### 5. Serwis notatek

**File**: `Notes/NoteService.cs` (nowy)

**Intent**: Zapisać notatkę, nadpisać istniejącą i dopisać właściwe zdarzenie do rejestru — tak, żeby strona nigdy nie rozmawiała z bazą sama.

**Contract**: Konstruktor bierze `UserScopedDbContextFactory`, `TimeProvider`, `ILogger<NoteService>` — **nigdy** `IDbContextFactory<>` (`DataAccessBoundaryTests` blokuje deploy). Metody:

- `GetByMaterialAsync(Guid materialId, CancellationToken)` → `Note?` — czym dysponuje strona materiału przy wejściu (czy istnieje już notatka, którą zapis zastąpi).
- `GetAsync(Guid noteId, CancellationToken)` → `Note?` — dla `/notes/{id}`.
- `AcceptAsync(...)` → typowany wynik z `Note` albo porażką — **akceptacja świeżego wygenerowania**: tworzy albo nadpisuje wiersz notatki dla tego materiału i dopisuje zdarzenie `Saved`.
- `UpdateAsync(...)` → typowany wynik — **ponowny zapis już zapisanej notatki**: aktualizuje `Title`, `Content`, `UpdatedAt` i **nie** dopisuje żadnego zdarzenia.
- `RecordGenerationAsync(...)` — dopisuje zdarzenie `Generated`; wołane wyłącznie po pomyślnym zakończeniu strumienia.

Rozdzielenie `AcceptAsync` od `UpdateAsync` jest tym, co utrzymuje wskaźnik akceptacji poniżej 100% — uzasadnienie w Critical Implementation Details.

`OwnerId` nie jest ustawiane ręcznie nigdzie — `AppDbContext.StampOwners` stempluje je z zalogowanego użytkownika i nadpisuje cokolwiek podano.

Wyścig na unikalnym indeksie `source_material_id` (dwie karty akceptujące naraz) łapiemy jako `DbUpdateException` i ponawiamy raz jako aktualizację istniejącego wiersza; drugie niepowodzenie zwraca porażkę zamiast rzucać — wzorem `GenerationQuotaService`, gdzie przegląd implementacji wykazał, że wyjątek wychodzący z metody zwracającej wynik trafia w stronie do gałęzi „coś poszło nie tak" i gubi właściwy komunikat.

Serwis waliduje przez `NoteValidator` przed zapisem — strona nie jest jedynym miejscem, w którym limit obowiązuje.

#### 6. Rejestracja i testy

**File**: `Program.cs`, `tests/10xNotes.Tests/NoteServiceTests.cs` (nowy)

**Intent**: Zarejestrować serwis jako scoped i udowodnić izolację właściciela, nadpisywanie 1:1 oraz to, że rejestr liczy dokładnie te zdarzenia, o których mowa.

**Contract**: `builder.Services.AddScoped<NoteService>();` obok `NoteGenerator` i `GenerationQuotaService`.

Testy na SQLite in-memory, wzorem `GenerationQuotaTests`: własne `SqliteConnection` w konstruktorze klasy, `EnsureCreated()`, `IDisposable`, prawdziwy `UserScopedDbContextFactory` owinięty wokół testowej fabryki i `StubCurrentUserAccessor`, `FixedClock : TimeProvider`, `NullLogger<T>.Instance`. Wartości domyślne Postgresa (`gen_random_uuid()`, `now()`) trzeba w seedach podać ręcznie — SQLite ich nie zna.

Przypadki: zapis tworzy notatkę powiązaną z materiałem; drugi `AcceptAsync` dla tego samego materiału **nadpisuje** i zostawia dokładnie jeden wiersz; notatka jednego użytkownika jest niewidoczna dla drugiego; `AcceptAsync` bez zalogowanego użytkownika rzuca; `AcceptAsync` dopisuje zdarzenie `Saved`, a `UpdateAsync` **nie** dopisuje żadnego; `RecordGenerationAsync` dopisuje `Generated`; `UpdateAsync` podbija `UpdatedAt` i zostawia `CreatedAt` bez zmian; `DraftContent` przeżywa edycję `Content`; treść ponad limitem jest odrzucona przez serwis, nie tylko przez UI.

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `NoteServiceTests` dowodzi nadpisywania 1:1, izolacji per użytkownik i tego, że `UpdateAsync` nie dopisuje zdarzenia akceptacji
- `DataAccessBoundaryTests` przechodzi — `NoteService` bierze `UserScopedDbContextFactory`
- Migracja stosuje się czysto: `dotnet ef database update`
- Wygenerowana migracja zawiera `ON DELETE NO ACTION` dla FK do `source_materials`

#### Manual Verification:

- Supabase security advisor nie zgłasza `notes` ani `note_events` jako tabel bez RLS (obowiązkowe wg `lessons.md`)
- Po starcie aplikacji obie tabele istnieją, a `/health/ready` zwraca `Healthy`

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej zatrzymaj się i poczekaj na potwierdzenie ręcznego testu przed Fazą 3.

---

## Phase 3: Edytor i zapis na stronie materiału

### Overview

Panel notatki na `/materials/{id}` po zakończeniu strumienia zamienia się w edytor z podglądem i przyciskiem zapisu. Zapis prowadzi na `/notes/{id}`.

### Changes Required:

#### 1. Komponent edytora

**File**: `Components/Notes/NoteEditor.razor` (nowy), `Components/Notes/NoteEditor.razor.css` (nowy)

**Intent**: Jedno miejsce, w którym mieszka edycja notatki, bo ten sam edytor obsługuje zapis po generowaniu (Faza 3) i ponowną edycję zapisanej notatki (Faza 4).

**Contract**: Parametry: tytuł i treść z powiązaniem dwukierunkowym, etykieta przycisku zapisu, flaga zajętości, `EventCallback` zapisu. Wewnątrz: pole tytułu, `<textarea>` ze źródłem Markdown, przełącznik **Edycja / Podgląd**, gdzie podgląd renderuje `NoteMarkdown.ToHtml(...)` przez `MarkupString`, oraz miejsce na komunikat walidacji.

`MarkupString` to jedyne wstrzyknięcie HTML-a w całym projekcie i jedyny powód, dla którego Faza 1 ma testy bezpieczeństwa — warto to napisać w komentarzu tutaj, w miejscu, w którym ktoś to zobaczy.

Komponent jest **bezstanowy poza swoimi parametrami** i nie dotyka bazy: strona woła serwis, komponent tylko zbiera dane. Bez tego rozdziału jedyna testowalna warstwa znika (brak bUnit).

#### 2. Panel notatki staje się edytorem

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Po zakończeniu strumienia oddać treść do edycji, nie zmieniając niczego w tym, jak strumień działa dziś.

**Contract**: Panel pozostaje `<pre class="note-content">` **w trakcie** generowania (tylko do odczytu — uzasadnienie w Critical Implementation Details). Po pomyślnym zakończeniu strumienia strona jednorazowo przenosi bufor do stanu edycji: tytuł z `NoteValidator.DeriveTitle(material.Title)`, treść z bufora, a oryginał zapamiętany osobno jako szkic do zapisania w `Note.DraftContent`. Dopiero wtedy renderowany jest `NoteEditor`.

Istniejące pola i przepływ generowania (`isGenerating`, dławiony repaint, `IAsyncDisposable`, `CancelGenerationAsync`, mapowanie `GenerationFailure` na polską kopię) zostają bez zmian. Porażka dalej czyści panel.

Po pomyślnym zakończeniu strumienia strona woła `NoteService.RecordGenerationAsync(...)` — to mianownik wskaźnika akceptacji.

#### 3. Zapis z potwierdzeniem zastąpienia

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Zapisać notatkę, ale nie pozwolić, żeby jedno kliknięcie po cichu skasowało wcześniej zaakceptowaną pracę.

**Contract**: Przy wejściu na stronę `OnInitializedAsync` woła `NoteService.GetByMaterialAsync(Id)` obok wczytania materiału, więc strona wie, czy notatka już istnieje. Kliknięcie zapisu przy istniejącej notatce pokazuje **potwierdzenie w treści strony** (blok z pytaniem i dwoma przyciskami, nie `window.confirm` — dialog przeglądarki blokuje pętlę zdarzeń i nie da się go ostylować ani przetłumaczyć). Potwierdzenie woła `NoteService.AcceptAsync(...)`, po czym `Navigation.NavigateTo($"/notes/{note.Id}")` — wzorem `Import.razor:132`.

Porażka walidacji lub zapisu mapuje typowany wynik na polską kopię, wzorem `MessageFor` z `Detail.razor:256`. Domyślna gałąź istnieje, żeby dodanie wartości do enuma nie zostawiło pustego alertu.

#### 4. Ochrona przed nadpisaniem niezapisanych zmian

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Nie pozwolić, żeby ponowne kliknięcie „Generuj notatkę" po cichu skasowało poprawki, których użytkownik jeszcze nie zapisał.

**Contract**: Strona trzyma informację, czy treść w edytorze różni się od tego, co przyszło z modelu. Kliknięcie generowania przy niezapisanych zmianach pokazuje to samo, wbudowane w stronę potwierdzenie co przy nadpisaniu notatki. Nawigacja poza stronę **nie** jest chroniona — decyzja świadoma, `NavigationLock` nie wchodzi w zakres.

#### 5. Wejście na stronę z istniejącą notatką

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Kiedy materiał ma już zapisaną notatkę, użytkownik ma się o tym dowiedzieć zanim cokolwiek wygeneruje.

**Contract**: Prawa kolumna przy istniejącej notatce pokazuje link do `/notes/{id}` i informację, że notatka dla tego materiału już istnieje. Przycisk generowania zostaje — regeneracja jest dozwolona, tylko zapis po niej pyta o potwierdzenie. To jedyna dziś droga do `/notes/{id}` poza przekierowaniem po zapisie (indeks notatek to S-03).

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `DataAccessBoundaryTests` przechodzi po dodaniu komponentu edytora i wstrzyknięć na stronie

#### Manual Verification:

- Po wygenerowaniu notatki panel zamienia się w edytor z polem tytułu wypełnionym tytułem materiału
- Przełącznik **Podgląd** pokazuje nagłówki i punkty jako strukturę, nie jako dosłowne `##` i `-`
- W trakcie generowania panel jest tylko do odczytu — nie da się w nim pisać
- Zapis prowadzi na `/notes/{id}`, a notatka przeżywa odświeżenie i wylogowanie
- Zapis przy istniejącej już notatce pyta o potwierdzenie zastąpienia i po odmowie niczego nie zmienia
- Ponowne generowanie przy niezapisanych poprawkach pyta o potwierdzenie
- Materiał źródłowy jest nietknięty po zapisie (guardrail PRD)
- Wejście na materiał, który ma już notatkę, pokazuje link do niej

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej zatrzymaj się i poczekaj na potwierdzenie ręcznego testu przed Fazą 4.

---

## Phase 4: Strona notatki

### Overview

`/notes/{id}` — materiał po lewej, zapisana notatka po prawej, edytowalna i zapisywalna w miejscu.

### Changes Required:

#### 1. Strona notatki

**File**: `Components/Pages/Notes/Detail.razor` (nowy), `Components/Pages/Notes/Detail.razor.css` (nowy)

**Intent**: Dać zapisanej notatce własny adres i pozwolić ją poprawić bez przechodzenia przez ponowne generowanie — które przy modelu 1:1 skasowałoby to, co użytkownik chce poprawić.

**Contract**: `@page "/notes/{Id:guid}"`, `@rendermode InteractiveServer`, `@attribute [Authorize]` zgodnie z resztą chronionych stron. Układ dwukolumnowy identyczny jak na stronie materiału (`row g-4` / `col-lg-6`): materiał źródłowy w `<pre>` po lewej, `NoteEditor` po prawej — to realizuje guardrail PRD „materiał zawsze pozostaje dostępny obok notatki" także tutaj.

Wczytanie: `NoteService.GetAsync(Id)`, a następnie materiał po `note.SourceMaterialId` przez `UserScopedDbContextFactory`. Brak notatki i cudza notatka renderują się **identycznie** — jak w `Detail.razor:127-133`, gdzie rozróżnienie pozwoliłoby sondować, które identyfikatory istnieją.

Zapis woła `NoteService.UpdateAsync(...)`, zostaje na stronie i pokazuje potwierdzenie z czasem zapisu przez `TimeProvider.ToDisplayTime(...)`. Bez potwierdzenia zastąpienia — to ta sama notatka, nie nadpisanie cudzej pracy — i bez zdarzenia akceptacji.

#### 2. Nawigacja

**File**: `Components/Pages/Notes/Detail.razor`

**Intent**: Domknąć pętlę tam i z powrotem między notatką a jej materiałem.

**Contract**: Link do `/materials/{note.SourceMaterialId}` w lewej kolumnie. Strony materiału i notatki wskazują na siebie nawzajem, co przy braku indeksu (S-03) jest jedyną nawigacją, jaką notatka ma.

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `DataAccessBoundaryTests` przechodzi po dodaniu nowej strony

#### Manual Verification:

- `/notes/{id}` pokazuje materiał obok notatki, obie kolumny z właściwą treścią
- Poprawka i ponowny zapis działają w miejscu; odświeżenie pokazuje zapisaną wersję
- Ponowny zapis **nie** zwiększa liczby zdarzeń `Saved` w rejestrze (sprawdzone zapytaniem do bazy)
- Notatka innego użytkownika i notatka nieistniejąca renderują się tak samo
- Niezalogowany użytkownik jest przekierowany na logowanie
- Linki między stroną materiału a stroną notatki działają w obie strony
- Pełna pętla przechodzi end-to-end: import → generowanie → poprawka → zapis → `/notes/{id}` → poprawka → zapis

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej zatrzymaj się i poczekaj na potwierdzenie ręcznego testu.

---

## Testing Strategy

### Unit Tests:

- `NoteValidatorTests` — każda wartość `NoteValidationFailure`, granice tytułu i treści, wyprowadzenie tytułu z przycięciem
- `NoteMarkdownTests` — struktura Markdownu na wyjściu; brak surowego HTML; neutralizacja `javascript:`; przepuszczenie `http`, `https`, `mailto`, adresów względnych i kotwic
- `NoteServiceTests` — nadpisywanie 1:1, izolacja per właściciel, rzut przy braku zalogowanego użytkownika, `AcceptAsync` dopisuje zdarzenie a `UpdateAsync` nie, `DraftContent` przeżywa edycję, walidacja egzekwowana w serwisie
- `DataAccessBoundaryTests` (istniejący) — obejmie nowe typy i strony automatycznie

### Integration Tests:

Brak nowych. Warstwa danych testowana na SQLite in-memory jak dotąd; `deploy.yml` uruchamia pakiet bez sieci, bez bazy i bez sekretów i to musi zostać. Postgresowe FK i RLS weryfikowane ręcznie względem prawdziwej bazy, jak przy `generation_quotas`.

Komponenty Razor pozostają nieprzetestowane — brak bUnit jest świadomym ograniczeniem projektu, a odpowiedzią na nie jest wypchnięcie każdej reguły do `NoteValidator`, `NoteMarkdown` i `NoteService`.

### Manual Testing Steps:

1. Zaimportuj plik `.md`, wygeneruj notatkę, poczekaj na koniec strumienia.
2. Sprawdź, że w trakcie generowania nie da się pisać w panelu, a po zakończeniu pojawia się edytor z wypełnionym tytułem.
3. Przełącz na **Podgląd** — nagłówki i punkty mają być strukturą, nie dosłownym `##`.
4. Popraw treść, zapisz — powinieneś wylądować na `/notes/{id}`.
5. Odśwież `/notes/{id}` i wyloguj/zaloguj się — notatka ma przetrwać (NFR trwałości).
6. Popraw notatkę na `/notes/{id}` i zapisz ponownie; sprawdź, że `note_events` **nie** urosło o kolejne `Saved`.
7. Wróć na materiał, wygeneruj ponownie, kliknij zapis — ma pojawić się pytanie o zastąpienie; odmów i sprawdź, że notatka jest nietknięta.
8. Wygeneruj, popraw bez zapisu, kliknij ponownie „Generuj" — ma pojawić się pytanie o niezapisane zmiany.
9. Wklej do notatki `<script>alert(1)</script>` i `[klik](javascript:alert(1))`, przełącz na Podgląd — nic się nie wykonuje, treść widnieje jako tekst.
10. Sprawdź w Supabase, że `notes` i `note_events` mają włączone RLS i że anon key ich nie czyta.
11. Sprawdź, że `/notes/{id}` cudzej notatki wygląda tak samo jak notatki nieistniejącej.
12. Sprawdź, że materiał źródłowy jest niezmieniony po całej pętli.

## Performance Considerations

Renderowanie Markdownu jest synchroniczne i wykonuje się przy każdym przełączeniu na Podgląd oraz przy każdym repaint w trybie podglądu. Notatka to najwyżej 64 KB, a `MarkdownPipeline` jest budowany raz i współdzielony, więc koszt jest nieistotny — pod warunkiem, że podgląd **nie** jest renderowany w trakcie strumienia, co i tak wyklucza decyzja o panelu tylko do odczytu.

Zapytania serwisu to pojedyncze wiersze po kluczu głównym albo po unikalnym indeksie `source_material_id`. Rejestr rośnie o jeden wiersz na wygenerowanie i jeden na akceptację — przy limicie 50 generowań dobowo na użytkownika to co najwyżej ~100 wierszy dziennie na użytkownika, bez potrzeby rotacji w MVP.

## Migration Notes

Dwie nowe tabele: `notes` i `note_events`. Migracja jest addytywna i wstecznie zgodna — rollback Coolify nie cofa migracji (`CLAUDE.md`), a starsza wersja aplikacji po prostu ich nie zna i działa dalej.

Nic nie trzeba backfillować: brak wiersza w `notes` znaczy „ten materiał nie ma jeszcze zaakceptowanej notatki", a pusty `note_events` znaczy „jeszcze nic nie zmierzono". Notatki wygenerowane przed tym wdrożeniem nie istnieją nigdzie, bo do tej pory były ulotne.

Żadnych nowych zmiennych środowiskowych ani sekretów.

## References

- Roadmapa: `context/foundation/roadmap.md` (S-01c, linie 129–140) — w tym otwarte pytanie 1:N vs nadpisanie, domknięte tutaj na nadpisanie
- PRD: `context/foundation/prd.md` — US-01 (domknięcie), FR-006, FR-008, Success Criteria (75% akceptacji), guardrail „materiał zawsze obok notatki"
- Otwarte pytania PRD: OQ2 (kaskada przy usuwaniu źródła) — celowo **nie** rozstrzygane; patrz Critical Implementation Details
- Lekcja o RLS: `context/foundation/lessons.md`
- Poprzedni plasterek: `context/archive/2026-08-17-ai-note-generation/plan.md`
- Odziedziczone ustalenie F9 (Markdown renderowany dosłownie): `context/archive/2026-08-17-ai-note-generation/reviews/impl-review.md:135-143`
- Precedens wyścigu i typowanego wyniku: `Generation/GenerationQuotaService.cs`, `context/archive/2026-08-17-ai-note-generation/reviews/impl-review.md:27-73`
- Wzorzec czystego szwu: `SourceMaterials/MarkdownImportValidator.cs`, `tests/10xNotes.Tests/MarkdownImportValidatorTests.cs`
- Wzorzec migracji z RLS: `Migrations/20260817174948_AddGenerationQuota.cs:52-58`
- Markdig — `DisableHtml()` i ostrzeżenie, że to nie sanitizer: dokumentacja `xoofx/markdig` (`site/docs/extensions/other.md`, `site/docs/usage.md`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Szew notatki (czysty)

#### Automated

- [x] 1.1 Kompilacja przechodzi: `dotnet build` — 616aa2c
- [x] 1.2 Cały pakiet testów przechodzi: `dotnet test` — 616aa2c
- [x] 1.3 `NoteMarkdownTests` dowodzi braku surowego HTML i neutralizacji `javascript:` — 616aa2c
- [x] 1.4 `NoteValidatorTests` pokrywa każdą wartość `NoteValidationFailure` — 616aa2c
- [x] 1.5 `DataAccessBoundaryTests` nadal przechodzi — 616aa2c

### Phase 2: Trwałość notatki i rejestr akceptacji

#### Automated

- [x] 2.1 Kompilacja przechodzi: `dotnet build`
- [x] 2.2 Cały pakiet testów przechodzi: `dotnet test`
- [x] 2.3 `NoteServiceTests` dowodzi nadpisywania 1:1, izolacji i braku zdarzenia przy `UpdateAsync`
- [x] 2.4 `DataAccessBoundaryTests` przechodzi — `NoteService` bierze `UserScopedDbContextFactory`
- [x] 2.5 Migracja stosuje się czysto: `dotnet ef database update`
- [x] 2.6 Migracja zawiera `ON DELETE NO ACTION` dla FK do `source_materials`

#### Manual

- [x] 2.7 Supabase security advisor nie zgłasza `notes` ani `note_events` bez RLS
- [x] 2.8 Obie tabele istnieją po starcie, `/health/ready` zwraca `Healthy`

### Phase 3: Edytor i zapis na stronie materiału

#### Automated

- [ ] 3.1 Kompilacja przechodzi: `dotnet build`
- [ ] 3.2 Cały pakiet testów przechodzi: `dotnet test`
- [ ] 3.3 `DataAccessBoundaryTests` przechodzi po dodaniu edytora i wstrzyknięć

#### Manual

- [ ] 3.4 Po generowaniu panel zamienia się w edytor z wypełnionym tytułem
- [ ] 3.5 Podgląd pokazuje strukturę, nie dosłowne `##` i `-`
- [ ] 3.6 W trakcie generowania panel jest tylko do odczytu
- [ ] 3.7 Zapis prowadzi na `/notes/{id}`, notatka przeżywa odświeżenie i wylogowanie
- [ ] 3.8 Zapis przy istniejącej notatce pyta o zastąpienie; odmowa niczego nie zmienia
- [ ] 3.9 Ponowne generowanie przy niezapisanych poprawkach pyta o potwierdzenie
- [ ] 3.10 Materiał źródłowy nietknięty po zapisie
- [ ] 3.11 Materiał z istniejącą notatką pokazuje link do niej

### Phase 4: Strona notatki

#### Automated

- [ ] 4.1 Kompilacja przechodzi: `dotnet build`
- [ ] 4.2 Cały pakiet testów przechodzi: `dotnet test`
- [ ] 4.3 `DataAccessBoundaryTests` przechodzi po dodaniu strony notatki

#### Manual

- [ ] 4.4 `/notes/{id}` pokazuje materiał obok notatki
- [ ] 4.5 Poprawka i ponowny zapis działają w miejscu
- [ ] 4.6 Ponowny zapis nie zwiększa liczby zdarzeń `Saved` w rejestrze
- [ ] 4.7 Cudza i nieistniejąca notatka renderują się identycznie
- [ ] 4.8 Niezalogowany użytkownik jest przekierowany na logowanie
- [ ] 4.9 Linki materiał ↔ notatka działają w obie strony
- [ ] 4.10 Pełna pętla import → generowanie → poprawka → zapis → poprawka → zapis przechodzi
