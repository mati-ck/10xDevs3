# Wklejenie tekstu → generowanie notatki (S-02) Implementation Plan

## Overview

Dodajemy drugie wejście do istniejącej pętli: użytkownik wkleja surowy tekst, który zapisuje się jako `SourceMaterial` tą samą drogą co zaimportowany plik `.md`. Od momentu zapisu wiersza nic się nie zmienia — `Materials/Detail.razor` generuje, edytuje i zapisuje notatkę bez wiedzy o tym, skąd wziął się materiał.

To realizuje FR-003 i domyka wejściową połowę pętli, której drugą połowę dostarczyły S-01a→S-01c.

## Current State Analysis

Pętla jest już generyczna względem `SourceMaterial`:

- `Materials/Detail.razor:229` czyta wiersz po `Id` i nic nie pyta o pochodzenie.
- `NotePrompt.BuildMessages` (`Generation/NotePrompt.cs:70`) bierze wyłącznie `Title` i `Content`.
- `NoteService`, `GenerationQuotaService`, `NoteEditor` — żadne z nich nie zna nazwy pliku.

Brakuje trzech rzeczy, i tylko trzech:

1. **Model nie potrafi opisać materiału bez pliku.** `SourceMaterial.OriginalFileName` jest `IsRequired().HasMaxLength(260)` (`Data/AppDbContext.cs:67`) i `NOT NULL` w bazie (`Migrations/20260817162727_AddSourceMaterial.cs:22`). `Materials/Detail.razor:46-47` bezwarunkowo renderuje „Zaimportowano … z pliku `<code>@material.OriginalFileName</code>`".
2. **Nie ma reguł dla wklejonego stringa.** `MarkdownImportValidator.Validate` przyjmuje `byte[]` + nazwę pliku i orzeka o rozszerzeniu oraz o poprawności UTF-8 — dwie rzeczy, które przy wklejaniu nie mogą wystąpić.
3. **Granica transportu jest wyprowadzona tylko z limitu notatki.** `NoteWireLimits.MaximumReceiveMessageSize` = `NoteValidator.MaxContentLength * 6 + 64 KB` = **458 752 B**. Import nigdy jej nie dotyka — `InputFile.OpenReadStream` (`Components/Pages/Materials/Import.razor:160`) idzie własnym, kawałkowanym kanałem interop. Textarea wysyła **całą wartość jako jedno wywołanie huba**, więc wklejka staje się największą rzeczą przechodzącą przez tę granicę.

### Key Discoveries:

- Wzorzec walidacji jest ustalony i konsekwentny: czysta klasa reguł + typowany enum porażki + polska kopia w komponencie (`SourceMaterials/MarkdownImportValidator.cs:15`, `Notes/NoteValidator.cs:12`). Powód jest podany wprost — projekt nie ma bUnit, więc cokolwiek zostanie w komponencie, nie da się przetestować.
- `Truncate` (bezpieczne wobec par zastępczych) istnieje **dwa razy**, skopiowane między `MarkdownImportValidator.cs:82` a `NoteValidator.cs:96`, z niemal identycznym komentarzem. Trzecia kopia w `PasteValidator` byłaby o jedną za dużo.
- Enum przechowywany jako tekst ma precedens: `NoteEvent.Kind` używa `.HasConversion<string>()` z uzasadnieniem, że jedynym konsumentem jest ręczne zapytanie SQL (`Data/AppDbContext.cs:143-146`).
- `lessons.md` § „An advertised limit must be one every layer beneath it can carry" opisuje dokładnie tę pułapkę, jeden plasterek wcześniej — i wymaga, żeby relacja między warstwami była **przypięta testem, nie komentarzem**. `NoteWireLimitTests` jest tym testem.
- `lessons.md` ostrzega w tym samym akapicie przed pomyłką jednostek: „a character count is not a byte count". Import mierzy **bajty** (`MaxFileSizeBytes`), notatka mierzy **znaki UTF-16** (`MaxContentLength`). Wklejka musi wybrać jedno i powiedzieć to głośno.
- Każdy zapis do `SourceMaterials` przechodzi przez `UserScopedDbContextFactory`; `OwnerId` jest stemplowany przez `AppDbContext.StampOwners` i nadpisywany niezależnie od tego, co poda wywołujący (`Data/AppDbContext.cs:279-283`). Wklejka nie wnosi tu nic nowego.
- Tabela `source_materials` już ma włączone RLS. Ta zmiana nie tworzy nowej tabeli, więc reguła „ENABLE ROW LEVEL SECURITY w tej samej migracji" nie ma tu zastosowania — ale migracja **nie może jej wyłączyć**.

## Desired End State

Zalogowany użytkownik wchodzi na `/materials/import`, przełącza się na zakładkę „Wklej tekst", wkleja materiał, poprawia podpowiedziany tytuł i zapisuje. Ląduje na `/materials/{id}`, gdzie widzi swój tekst obok przycisku „Generuj notatkę" — dalej wszystko działa dokładnie tak, jak dla pliku `.md`.

Weryfikacja: pełna ścieżka wklejka → generowanie → edycja → zapis kończy się notatką pod `/notes/{id}`, a `dotnet test` przechodzi z nowymi testami `PasteValidator` i rozszerzonym `NoteWireLimitTests`.

## What We're NOT Doing

- **Nie budujemy listy materiałów ani notatek** — to S-03 (`browse-notes-and-sources`). Jedyną drogą do materiału pozostaje przekierowanie po zapisie i URL.
- **Nie dotykamy edycji materiału źródłowego** — S-04 jest zablokowane otwartym pytaniem PRD nr 1.
- **Nie dotykamy usuwania** — S-05/S-06.
- **Nie zmieniamy prompta, modelu ani `NotePrompt.Version`.** Wklejony materiał trafia do dokładnie tego samego prompta; podbicie wersji rozspójniłoby pomiar akceptacji z S-01b.
- **Nie normalizujemy CRLF ani nie ścinamy BOM przy wklejaniu.** Import ścina BOM, bo BOM jest artefaktem kodowania pliku; schowek go nie niesie. Rozjechanie wejść w drugą stronę byłoby gorsze niż brak obu.
- **Nie zmieniamy limitu importu** (128 KB bajtów) ani limitu notatki (64 K znaków).
- **Nie dodajemy parsowania Markdowna po stronie wklejki.** Tekst jest zapisywany dosłownie, tak jak treść zaimportowanego pliku.

## Implementation Approach

Trzy fazy, każda zostawiająca aplikację, która się buduje i ma zielone testy:

1. **Schemat najpierw** — model musi umieć opisać materiał bez pliku, zanim cokolwiek taki materiał utworzy. Po fazie 1 nikt jeszcze nie wkleja, ale istniejące wiersze mają jawny `kind`, a strona szczegółów przestaje zakładać istnienie nazwy pliku.
2. **Czyste reguły i granica transportu** — `PasteValidator` i przeliczenie granicy huba żyją bez UI i są w całości testowalne. Ta faza jest jedynym miejscem, w którym limit tekstu zamienia się na limit bajtów.
3. **UI na końcu** — dopiero gdy model i reguły są na miejscu, `Import.razor` dostaje drugie wejście.

Kolejność jest odwrotna do naturalnej pokusy („najpierw pokaż pole tekstowe"), i celowo: gdyby UI powstało pierwsze, nie miałoby gdzie zapisać wyniku ani czym go zwalidować.

## Critical Implementation Details

**Granica transportu.** Textarea z wklejką przechodzi przez SignalR jako jedno wywołanie huba, więc podniesienie limitu tekstu do 128 K znaków bez podniesienia `MaximumReceiveMessageSize` daje awarię o najgorszym kształcie: obwód jest zrywany, użytkownik dostaje modal ponownego łączenia i traci wklejony materiał, a komponent nie może tego przechwycić. Nowa wartość to `max(65536, 131072) * 6 + 65536` = **851 968 B**. Podniesienie granicy zwiększa też ilość pamięci, jaką jedna ramka może zająć na obwód — akceptowalne, bo trasa jest za `[Authorize]`, ale to realna zmiana zasięgu skutków.

**Jednostki.** `MarkdownImportValidator.MaxFileSizeBytes` liczy **bajty**, `PasteValidator.MaxContentLength` liczy **jednostki kodu UTF-16**. Obie wartości to `128 * 1024`, ale nie znaczą tego samego: plik 128 KB z polskim tekstem to ~64 K znaków. To świadoma decyzja (użytkownik ma móc wkleić treść dokumentu, który mógłby zaimportować), a nie przeoczenie — musi być udokumentowana w kodzie, bo inaczej ktoś „naprawi" ją na zgodność.

**Kolejność w fazie 1.** Backfill `kind` musi wykonać się **przed** nałożeniem `NOT NULL` na tę kolumnę, w tej samej migracji. Kolumna `original_file_name` idzie w drugą stronę — traci `NOT NULL` i nie wymaga backfillu.

---

## Phase 1: Proweniencja materiału bez pliku

### Overview

Model i baza uczą się opisywać materiał, który nie pochodzi z pliku. Nikt jeszcze takiego nie tworzy — ta faza tylko otwiera na niego miejsce i naprawia kopię, która zakłada istnienie nazwy pliku.

### Changes Required:

#### 1. Typ proweniencji

**File**: `Data/Entities/SourceMaterialKind.cs` (nowy)

**Intent**: Nazwać wprost, skąd wziął się materiał, zamiast wnioskować to z pustej lub nieobecnej nazwy pliku. Idzie za wzorcem `NoteEventKind` — enum przechowywany jako tekst, żeby ręczne zapytanie SQL było czytelne.

**Contract**: `public enum SourceMaterialKind { MarkdownFile = 0, Paste = 1 }` w `_10xnotes.Data.Entities`. `MarkdownFile` jest wartością zerową, bo to stan wszystkich istniejących wierszy.

#### 2. Encja

**File**: `Data/Entities/SourceMaterial.cs`

**Intent**: Dodać `Kind` i dopuścić brak nazwy pliku dla wklejki. Komentarz przy `OriginalFileName` musi powiedzieć, że `null` znaczy „nie z pliku", a nie „nie udało się odczytać nazwy".

**Contract**: `public SourceMaterialKind Kind { get; set; }` oraz `OriginalFileName` zmienia typ na `string?` z domyślnym `null` (dziś: `string` = `string.Empty`).

#### 3. Konfiguracja EF

**File**: `Data/AppDbContext.cs`

**Intent**: Odzwierciedlić jedno i drugie w modelu — `Kind` jako wymagany tekst z konwersją, `OriginalFileName` przestaje być wymagane.

**Contract**: w bloku `modelBuilder.Entity<SourceMaterial>` (linie 61-74): `entity.Property(m => m.Kind).IsRequired().HasMaxLength(20).HasConversion<string>();` oraz `entity.Property(m => m.OriginalFileName).HasMaxLength(260);` — bez `IsRequired()`.

#### 4. Migracja

**File**: `Migrations/<timestamp>_AddSourceMaterialKind.cs` (generowana przez `dotnet ef migrations add AddSourceMaterialKind`)

**Intent**: Dodać kolumnę `kind`, przypisać istniejącym wierszom `MarkdownFile`, dopiero potem nałożyć `NOT NULL`; zdjąć `NOT NULL` z `original_file_name`. Migracja musi być zgodna wstecz — rollback w Coolify nie cofa migracji (`CLAUDE.md`).

**Contract**: EF wygeneruje `AddColumn` z `defaultValue: ""` — to trzeba poprawić ręcznie na sekwencję, w której backfill poprzedza ograniczenie. Kolejność, bo tylko ona jest tu nieoczywista:

```sql
ALTER TABLE public.source_materials ADD COLUMN kind character varying(20);
UPDATE public.source_materials SET kind = 'MarkdownFile' WHERE kind IS NULL;
ALTER TABLE public.source_materials ALTER COLUMN kind SET NOT NULL;
ALTER TABLE public.source_materials ALTER COLUMN original_file_name DROP NOT NULL;
```

`Down` odwraca oba kroki, przy czym przywrócenie `NOT NULL` na `original_file_name` wymaga wcześniejszego `UPDATE … SET original_file_name = '' WHERE original_file_name IS NULL` — inaczej rollback wywróci się na wierszach z wklejek. Migracja **nie dotyka** RLS: tabela ma je włączone od `AddSourceMaterial` i tak ma zostać.

#### 5. Kopia na stronie szczegółów

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Zdanie o pochodzeniu rozgałęzia się na `Kind`, bo dla wklejki nie ma pliku do pokazania. Data importu zostaje w obu wariantach.

**Contract**: linie 42-48 — dla `SourceMaterialKind.MarkdownFile` zostaje obecne „Zaimportowano … z pliku `<code>…</code>`"; dla `Paste` odpowiednik bez części o pliku (np. „Wklejono @…"). Formatowanie daty przez `TimeProvider.ToDisplayTime` i `PolishCulture` bez zmian.

### Success Criteria:

#### Automated Verification:

- Projekt się kompiluje: `dotnet build`
- Zestaw testów przechodzi: `dotnet test`
- Migracja jest spójna z modelem: `dotnet ef migrations has-pending-model-changes` nie zgłasza zmian
- `DataAccessBoundaryTests` przechodzi (żaden nowy typ nie wstrzykuje `DbContext` ani `IDbContextFactory<>`)

#### Manual Verification:

- Migracja stosuje się na kopii bazy z istniejącymi materiałami i wszystkie dostają `kind = 'MarkdownFile'`
- Wcześniej zaimportowany materiał wciąż pokazuje na `/materials/{id}` zdanie z nazwą pliku
- Advisor bezpieczeństwa Supabase nie zgłasza nowych ostrzeżeń dla `source_materials` (RLS wciąż włączone)

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej — zatrzymaj się i poczekaj na potwierdzenie testów ręcznych, zanim ruszysz dalej. Migracja dotyka tabeli z danymi użytkowników.

---

## Phase 2: Reguły wklejania i granica transportu

### Overview

Czyste reguły dla wklejonego stringa oraz przeliczenie granicy huba tak, żeby uniosła największą rzecz, jaką aplikacja przez nią przepuszcza. Bez UI — wszystko w tej fazie jest testowalne bez przeglądarki.

### Changes Required:

#### 1. Wspólny helper przycinania

**File**: `SourceMaterials/TextLimits.cs` (nowy)

**Intent**: Wyciągnąć bezpieczne wobec par zastępczych przycinanie, które dziś istnieje w dwóch kopiach, zamiast dodawać trzecią. Komentarz o samotnej parze zastępczej przenosi się tutaj, do jedynego egzemplarza.

**Contract**: `internal static string Truncate(string value, int maxLength)` — zachowanie identyczne z `MarkdownImportValidator.Truncate` (`MarkdownImportValidator.cs:82`). `MarkdownImportValidator` i `NoteValidator` zaczynają go wywoływać i tracą swoje prywatne kopie; ich publiczne zachowanie się nie zmienia, co pilnują istniejące testy.

#### 2. Wynik i przyczyny odrzucenia

**File**: `SourceMaterials/PasteResult.cs` (nowy)

**Intent**: Typowana odpowiedź na „czy ta wklejka może zostać materiałem", w tej samej formie co `MarkdownImportResult`. Jeden człon enuma na jeden komunikat widoczny dla użytkownika, bez członu-śmietnika.

**Contract**: `enum PasteFailure { None = 0, Empty, TooLong }` oraz `sealed record PasteResult` z `Succeeded`, `Content`, `FailureReason` i fabrykami `Success(string)` / `Failure(PasteFailure)`. Dokładnie dwie przyczyny: rozszerzenie i UTF-8 nie mogą wystąpić dla stringa, który już jest stringiem.

#### 3. Reguły wklejania

**File**: `SourceMaterials/PasteValidator.cs` (nowy)

**Intent**: Zebrać wszystko, co decyduje, czy wklejony tekst staje się materiałem i jak się nazywa. Czyste — bez EF, bez komponentów — bo bez bUnit to jedyny sposób, żeby te gałęzie w ogóle miały test.

**Contract**:
- `public const int MaxContentLength = 128 * 1024;` — **jednostki kodu UTF-16**, nie bajty. XML-doc musi wprost powiedzieć, że to inna jednostka niż `MarkdownImportValidator.MaxFileSizeBytes`, i dlaczego obie mają tę samą wartość liczbową.
- `public const string FallbackTitle` — ta sama treść co `MarkdownImportValidator.FallbackTitle` („Materiał źródłowy"), dla wklejki bez użytecznej pierwszej linii.
- `public static string DeriveTitle(string? content)` — pierwsza niepusta linia, po zdjęciu wiodących `#` i białych znaków, przycięta przez `TextLimits.Truncate` do 200 i `TrimEnd()`; `FallbackTitle`, gdy nic nie zostaje.
- `public static PasteResult Validate(string? content)` — kolejność jak w `MarkdownImportValidator.Validate`: najpierw `Empty` (`string.IsNullOrWhiteSpace`), potem `TooLong` (`Length > MaxContentLength`). Treść zwracana **dosłownie**, bez `Trim()` — wiodące białe znaki są znaczące w Markdownie, tak samo jak w `NoteValidator.Validate` (`NoteValidator.cs:70-72`).

Sprawdzenie długości jest po stronie serwera niezależnie od `maxlength` w przeglądarce: `maxlength` to wygoda dla użytkownika, nie zabezpieczenie, a wartość ponad limit zrywa obwód zamiast zwrócić błąd.

#### 4. Granica transportu

**File**: `Notes/NoteWireLimits.cs` → `SourceMaterials/…` lub neutralna lokalizacja; typ zmienia nazwę na `HubWireLimits`

**Intent**: Klasa przestaje dotyczyć wyłącznie notatek — od tej fazy ogranicza dwie rzeczy, a wklejka jest większą z nich. Nazwa i miejsce mają to odzwierciedlać, żeby następna osoba nie wyprowadziła granicy tylko z limitu notatki (czyli nie powtórzyła tego samego błędu trzeci raz).

**Contract**: `MaximumReceiveMessageSize = Math.Max(NoteValidator.MaxContentLength, PasteValidator.MaxContentLength) * WorstCaseBytesPerChar + FramingHeadroom`. `WorstCaseBytesPerChar = 6` i `FramingHeadroom = 64 * 1024` bez zmian. Wartość rośnie z 458 752 do **851 968**. `Math.Max` nie jest wyrażeniem stałym, więc pole przestaje być `const` i staje się `static readonly int` — `Program.cs` używa go w wywołaniu metody, więc to zadziała bez dalszych zmian.

#### 5. Wpięcie granicy

**File**: `Program.cs`

**Intent**: Zaktualizować nazwę typu i komentarz nad `AddHubOptions` (linie 23-30), który dziś mówi wyłącznie o notatkach i o limicie 64 KB.

**Contract**: `options.MaximumReceiveMessageSize = HubWireLimits.MaximumReceiveMessageSize;` — wywołanie zostaje, bo jego usunięcie po cichu przywraca domyślne 32 KB.

#### 6. Testy reguł

**File**: `tests/10xNotes.Tests/PasteValidatorTests.cs` (nowy)

**Intent**: Przypiąć każdą gałąź, która decyduje, co trafia do bazy i do prompta — wzorem `MarkdownImportValidatorTests`.

**Contract**: pokrycie `Validate` (poprawna wklejka, pusty string, `null`, same białe znaki, dokładnie na limicie, jeden znak ponad limit, brak `Trim()` na treści) oraz `DeriveTitle` (zwykły pierwszy wiersz, wiersz z `#`/`##`, wiodące puste wiersze, same białe znaki → `FallbackTitle`, pierwszy wiersz dłuższy niż 200 znaków, pierwszy wiersz kończący się parą zastępczą na granicy cięcia).

#### 7. Testy granicy

**File**: `tests/10xNotes.Tests/NoteWireLimitTests.cs`

**Intent**: Rozszerzyć istniejący test tak, żeby pilnował **obu** limitów, nie tylko notatki. Bez tego arytmetyka wklejki nie ma nic, co by ją utrzymało — a to jest dokładnie ten test, którego brak opisuje `lessons.md`.

**Contract**: dodać przypadek analogiczny do `The_wire_limit_carries_the_largest_note_the_validator_accepts`, ale dla `PasteValidator.MaxContentLength` (najgorszy przypadek: `new string('', …)`, plus wariant z polskim `ż`). Istniejące asercje dla notatki zostają. Nazwa pliku może pozostać, jeśli klasa nadal pokrywa notatkę — ale jej XML-doc musi wspomnieć oba limity.

### Success Criteria:

#### Automated Verification:

- Projekt się kompiluje: `dotnet build`
- Cały zestaw przechodzi: `dotnet test`
- `PasteValidatorTests` pokrywa obie przyczyny odrzucenia i wszystkie gałęzie `DeriveTitle`
- `NoteWireLimitTests` przechodzi dla wklejki na pełnym limicie i nadal wykazuje, że domyślna wartość SignalR by jej nie uniosła
- `MarkdownImportValidatorTests` i `NoteValidatorTests` przechodzą bez zmian po przeniesieniu `Truncate`

#### Manual Verification:

- Wyliczona wartość `MaximumReceiveMessageSize` to 851 968 (weryfikacja przez debugger lub tymczasowy log przy starcie)
- Aplikacja startuje i istniejąca ścieżka zapisu notatki 64 KB nadal działa (regresja granicy huba)

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej — zatrzymaj się i poczekaj na potwierdzenie testów ręcznych przed fazą 3.

---

## Phase 3: Przełącznik wejścia na `/materials/import`

### Overview

Strona importu dostaje drugie wejście. Obie zakładki dzielą pole tytułu, obsługę błędu zapisu i przekierowanie — różnią się wyłącznie tym, skąd bierze się treść.

### Changes Required:

#### 1. Strona dodawania materiału

**File**: `Components/Pages/Materials/Import.razor`

**Intent**: Przełącznik między „Wklej tekst" a „Plik .md" nad wspólnym formularzem; wybrana zakładka decyduje, który walidator biegnie i jaki `Kind` dostaje wiersz. Ścieżka zapisu (`UserScopedDbContextFactory`, `catch (DbUpdateException)`, przekierowanie na `/materials/{id}`) pozostaje jedna dla obu.

**Contract**:
- Stan zakładki jako prywatne pole enumowe; przełącznik w stylu `btn-group` z `NoteEditor.razor:17-26`, domyślnie **„Wklej tekst"** (niższy próg wejścia, zgodnie z uzasadnieniem FR-003 w PRD).
- `<textarea>` z `maxlength="@PasteValidator.MaxContentLength"`, wiązana przez `@onchange` (nie `@oninput`) — każde naciśnięcie klawisza przy 128 K znaków byłoby round-tripem po SignalR; `NoteEditor.razor:38-40` niesie to samo uzasadnienie. Licznik znaków jak w `NoteEditor.razor:50-52`.
- Podpowiedź tytułu: przy zmianie treści wklejki wywołaj `PasteValidator.DeriveTitle` i wypełnij `Input.Title` **tylko** wtedy, gdy pole jest puste albo wciąż trzyma poprzednio podpowiedzianą wartość — dokładnie ten sam kontrakt, który `autoFilledTitle` realizuje dla nazwy pliku (`Import.razor:65-75`). Pole `autoFilledTitle` obsługuje obie zakładki.
- Przy zapisie z zakładki wklejki: `Kind = SourceMaterialKind.Paste`, `OriginalFileName = null`. Przy zapisie z pliku: `Kind = SourceMaterialKind.MarkdownFile` i dotychczasowa wartość.
- Nowe `MessageFor(PasteFailure)` obok istniejącego `MessageFor(MarkdownImportFailure)`: `Empty` → wariant „Nie ma z czego zrobić notatki", `TooLong` → komunikat podający limit **w znakach** (nie w KB — jednostka musi zgadzać się z licznikiem obok pola).
- Przełączenie zakładki czyści `errorMessage`, ale **nie czyści** wpisanej treści ani wybranego pliku: użytkownik, który zajrzał na drugą zakładkę, nie powinien tracić tego, co już wkleił.

#### 2. Kopia nagłówka i nawigacji

**File**: `Components/Pages/Materials/Import.razor`, `Components/Layout/NavMenu.razor`

**Intent**: Strona nie dotyczy już wyłącznie importu pliku, więc nagłówek, akapit wstępny i pozycja w menu mają mówić o dodawaniu materiału.

**Contract**: `<PageTitle>`, `<h1>` i akapit z `Import.razor:17-24` opisują oba wejścia; zdanie o maksymalnym rozmiarze pojawia się w kontekście właściwej zakładki (KB dla pliku, znaki dla wklejki). `NavMenu.razor:20-22` — tekst pozycji przestaje mówić wyłącznie „Zaimportuj materiał". **Trasa `/materials/import` zostaje bez zmian**: jest już w produkcji i zmiana adresu nie kupuje nic poza zepsutymi zakładkami.

### Success Criteria:

#### Automated Verification:

- Projekt się kompiluje: `dotnet build`
- Cały zestaw przechodzi: `dotnet test`
- `DataAccessBoundaryTests` przechodzi (strona nadal korzysta z `UserScopedDbContextFactory`)

#### Manual Verification:

- Wklejenie kilku akapitów tekstu, zapis, przekierowanie na `/materials/{id}` z widoczną treścią i zdaniem o pochodzeniu bez nazwy pliku
- „Generuj notatkę" na wklejonym materiale streamuje notatkę i zapisuje ją pod `/notes/{id}`
- Pusta wklejka i wklejka z samych spacji dają polski komunikat, a nie zapis
- Wklejenie ~130 K znaków: przeglądarka blokuje nadmiar przez `maxlength`, a obwód **nie** zostaje zerwany (brak modala ponownego łączenia)
- Podpowiedź tytułu wypełnia się z pierwszej linii i **nie** nadpisuje tytułu wpisanego ręcznie po zmianie treści
- Zakładka „Plik .md" działa dokładnie jak przed zmianą, łącznie z odrzuceniem `.txt` i pliku za dużego
- Przełączenie zakładki tam i z powrotem nie gubi wklejonego tekstu

**Implementation Note**: To ostatnia faza — po weryfikacji ręcznej plasterek jest gotowy do PR.

---

## Testing Strategy

### Unit Tests:

- `PasteValidator.Validate`: poprawna treść, `null`, pusty string, same białe znaki, dokładnie na limicie, limit + 1, zachowanie wiodących białych znaków
- `PasteValidator.DeriveTitle`: zwykła pierwsza linia, `#`/`##` na początku, wiodące puste wiersze, same białe znaki, pierwsza linia > 200 znaków, cięcie na parze zastępczej
- `TextLimits.Truncate` pośrednio, przez istniejące `MarkdownImportValidatorTests` i `NoteValidatorTests` — brak zmian w ich asercjach jest dowodem, że refaktor był bezpieczny
- `HubWireLimits`: najgorszy przypadek wklejki i notatki mieści się w granicy; domyślna wartość SignalR by się nie zmieściła

### Integration Tests:

Brak nowych. Ścieżka bazodanowa (`UserScopedDbContextFactory` → `AppDbContext` → `source_materials`) jest już pokryta przez `OwnerScopingTests` i nie zmienia się — wklejka wstawia wiersz tą samą drogą co import. Poprawność `kind` i nullowalności weryfikowana ręcznie, zgodnie z decyzją o zakresie testów.

### Manual Testing Steps:

1. Zaloguj się, wejdź na `/materials/import` — domyślnie widoczna zakładka „Wklej tekst"
2. Wklej kilka akapitów; sprawdź, że tytuł wypełnił się z pierwszej linii i że licznik znaków rośnie
3. Popraw tytuł ręcznie, zmień treść — tytuł ma zostać nietknięty
4. Zapisz; sprawdź przekierowanie i zdanie o pochodzeniu bez nazwy pliku
5. Kliknij „Generuj notatkę", poczekaj na stream, popraw i zapisz — powinieneś wylądować na `/notes/{id}`
6. Wróć na `/materials/import`, spróbuj zapisać pustą wklejkę i wklejkę z samych spacji
7. Wklej ~130 K znaków (np. przez konsolę przeglądarki, żeby ominąć `maxlength`) i zapisz — oczekiwany polski komunikat, **bez** modala ponownego łączenia
8. Przełącz na „Plik .md", zaimportuj plik jak wcześniej; sprawdź, że jego strona nadal pokazuje nazwę pliku
9. Otwórz materiał zaimportowany przed tą zmianą i potwierdź, że jego zdanie o pochodzeniu jest nienaruszone

## Performance Considerations

Podniesienie `MaximumReceiveMessageSize` z 448 KB do 832 KB podwaja ilość pamięci, jaką pojedyncza ramka może zająć na obwód. Trasa jest za `[Authorize]`, a `target_scale` w PRD to `users: medium`, `qps: low` — to mieści się w budżecie. Warto odnotować, że to sufit na ramkę, nie stała alokacja: zwykłe interakcje nadal ważą kilobajty.

Wklejka 128 K znaków trafia do prompta w całości, tak jak zaimportowany plik. `MarkdownImportValidator` uzasadnia swój limit tym, że utrzymuje materiał w oknie kontekstu modelu; limit wklejki jest liczony w znakach, więc w skrajnym przypadku (tekst ASCII) niesie ~2× więcej tokenów niż plik na swoim limicie. `NoteGenerator` już klasyfikuje odpowiedź „za długi kontekst" jako `GenerationFailure.TooLong` i pokazuje polski komunikat (`NoteGenerator.cs:180-188`), więc najgorszy przypadek jest obsłużony, a nie niespodzianką.

## Migration Notes

Jedna migracja, zgodna wstecz w obie strony. Wersja aplikacji sprzed zmiany działa na schemacie po migracji: `kind` ma wartość we wszystkich wierszach, a stary kod po prostu jej nie czyta; `original_file_name` staje się nullowalne, ale stary kod nigdy nie wstawia `null`. To jest wymóg — rollback w Coolify nie cofa migracji (`CLAUDE.md`).

`Down` musi wypełnić `original_file_name` pustym stringiem dla wierszy z wklejek, zanim przywróci `NOT NULL` — inaczej wycofanie migracji wywróci się na danych, które sama umożliwiła.

## References

- Roadmapa: `context/foundation/roadmap.md` § S-02
- PRD: `context/foundation/prd.md` § FR-003
- Reguła o limitach przez warstwy: `context/foundation/lessons.md` § „An advertised limit must be one every layer beneath it can carry"
- Wzorzec walidatora: `SourceMaterials/MarkdownImportValidator.cs:15`
- Wzorzec enuma jako tekstu: `Data/AppDbContext.cs:143-146`
- Wzorzec przełącznika w UI: `Components/Notes/NoteEditor.razor:17-26`
- Poprzedni plasterek: `context/archive/2026-08-17-note-review-save/`

## Progress

> Konwencja: `- [ ]` do zrobienia, `- [x]` zrobione. Dopisz ` — <commit sha>`, gdy krok wyląduje. Nie zmieniaj tytułów kroków. Zobacz `references/progress-format.md`.

### Phase 1: Proweniencja materiału bez pliku

#### Automated

- [x] 1.1 Projekt się kompiluje: `dotnet build` — 7f709af
- [x] 1.2 Zestaw testów przechodzi: `dotnet test` — 7f709af
- [x] 1.3 `dotnet ef migrations has-pending-model-changes` nie zgłasza zmian — 7f709af
- [x] 1.4 `DataAccessBoundaryTests` przechodzi — 7f709af

#### Manual

- [ ] 1.5 Migracja stosuje się na kopii bazy i wszystkie istniejące wiersze dostają `kind = 'MarkdownFile'`
- [ ] 1.6 Wcześniej zaimportowany materiał wciąż pokazuje zdanie z nazwą pliku
- [ ] 1.7 Advisor bezpieczeństwa Supabase nie zgłasza nowych ostrzeżeń dla `source_materials`

### Phase 2: Reguły wklejania i granica transportu

#### Automated

- [x] 2.1 Projekt się kompiluje: `dotnet build` — fa9b9e9
- [x] 2.2 Cały zestaw przechodzi: `dotnet test` — fa9b9e9
- [x] 2.3 `PasteValidatorTests` pokrywa obie przyczyny odrzucenia i wszystkie gałęzie `DeriveTitle` — fa9b9e9
- [x] 2.4 `NoteWireLimitTests` przechodzi dla wklejki na pełnym limicie i nadal wykazuje niewystarczalność domyślnej wartości SignalR — fa9b9e9
- [x] 2.5 `MarkdownImportValidatorTests` i `NoteValidatorTests` przechodzą bez zmian po przeniesieniu `Truncate` — fa9b9e9

#### Manual

- [x] 2.6 Wyliczona wartość `MaximumReceiveMessageSize` to 851 968 — fa9b9e9
- [ ] 2.7 Aplikacja startuje i zapis notatki 64 KB nadal działa

### Phase 3: Przełącznik wejścia na `/materials/import`

#### Automated

- [x] 3.1 Projekt się kompiluje: `dotnet build`
- [x] 3.2 Cały zestaw przechodzi: `dotnet test`
- [x] 3.3 `DataAccessBoundaryTests` przechodzi

#### Manual

- [ ] 3.4 Wklejenie tekstu, zapis i przekierowanie na `/materials/{id}` bez nazwy pliku w kopii
- [ ] 3.5 „Generuj notatkę" na wklejonym materiale streamuje i zapisuje notatkę
- [ ] 3.6 Pusta wklejka i wklejka z samych spacji dają polski komunikat, a nie zapis
- [ ] 3.7 ~130 K znaków daje komunikat, a nie zerwany obwód
- [ ] 3.8 Podpowiedź tytułu nie nadpisuje tytułu wpisanego ręcznie
- [ ] 3.9 Zakładka „Plik .md" działa dokładnie jak przed zmianą
- [ ] 3.10 Przełączenie zakładki nie gubi wklejonego tekstu
