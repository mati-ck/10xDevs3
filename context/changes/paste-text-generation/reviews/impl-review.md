<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Wklejenie tekstu → generowanie notatki (S-02)

- **Plan**: `context/changes/paste-text-generation/plan.md`
- **Scope**: Phases 1-3 of 3 (wszystkie fazy)
- **Date**: 2026-08-27
- **Verdict**: REJECTED → wszystkie ustalenia naprawione 2026-08-27
- **Findings**: 1 critical, 3 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

Kryteria automatyczne (1.1-1.4, 2.1-2.5, 3.1-3.3) uruchomione i zielone. Kryterium manualne 2.6 odhaczone w planie — zweryfikowane niezależnie, nie jest podbite na słowo: `HubWireLimits.MaximumReceiveMessageSize` jest wyliczany ze stałych, a nie wpisany liczbą. Pozostałe kryteria manualne są otwarte i oczekują na weryfikację w przeglądarce.

## Findings

### F1 — Rollback wywraca odczyt materiałów, wbrew deklaracji migracji

- **Severity**: CRITICAL
- **Impact**: HIGH — stawka architektoniczna; przemyśl, zanim zdecydujesz
- **Dimension**: Safety & Quality
- **Location**: `Migrations/20260827112147_AddSourceMaterialKind.cs:52-53` (deklaracja w `:17-22`)
- **Detail**: Migracja zdejmuje `NOT NULL` z `original_file_name` i deklaruje w komentarzu, że wersja aplikacji sprzed zmiany "runs fine against the schema after it", uzasadniając to tym, że stary kod nigdy nie wstawia `null`. Argument pokrywa wyłącznie **zapis**. Model sprzed plasterka mapuje kolumnę jako `IsRequired()` na nienullowalnym `string`, a EF Core rzuca przy materializacji `NULL` do takiej właściwości. Zweryfikowane empirycznie w tym repo (EF Core 10, SQLite, model `IsRequired()` nad nullowalną kolumną z wierszem `NULL`): `InvalidOperationException: The data is NULL at ordinal 1.` Rollback w Coolify cofa kod, ale nie migrację — więc po pierwszym wklejeniu przez dowolnego użytkownika wycofana aplikacja wywraca się na stronie materiału i na liście materiałów. To narusza wprost regułę z `CLAUDE.md`: "A Coolify rollback does not reverse migrations — keep them backward-compatible."
- **Fix A (Recommended)**: Zapisywać `""` zamiast `null` dla wklejek w `Import.razor`; proweniencję niesie wyłącznie `Kind`.
  - Strength: Przywraca zgodność z twardą regułą projektu bez nowej migracji i bez zmiany kontraktu typu. Encja i tak instruuje (`SourceMaterial.cs:47-56`), żeby czytać `Kind`, a nie wnioskować z braku nazwy pliku — więc `""` niczego nie zaciemnia.
  - Tradeoff: Nullowalność kolumny staje się nieużywana, co podważa sens zdejmowania `NOT NULL` w ogóle. Wymaga testu pilnującego, że wklejka nie zapisuje `null`.
  - Confidence: HIGH — jednolinijkowa zmiana, a zachowanie EF potwierdzone eksperymentem.
  - Blind spot: Nie sprawdzono, czy Supabase ma już wiersze z `NULL` z ręcznych testów; jeśli tak, potrzebny jednorazowy `UPDATE`.
- **Fix B**: Zostawić `null`, skreślić fałszywą deklarację i zapisać warunek rollbacku jako procedurę.
  - Strength: Zachowuje świadomą decyzję modelową (`null` = "nie z pliku") i nie dokłada semantyki pustemu stringowi.
  - Tradeoff: Kupuje czystość modelu kosztem złamania reguły deployu — rollback przestaje być operacją jednym kliknięciem i wymaga ręcznego `UPDATE public.source_materials SET original_file_name = '' WHERE original_file_name IS NULL;` przed wycofaniem.
  - Confidence: MEDIUM — działa, ale opiera się na tym, że ktoś pod presją incydentu przeczyta procedurę.
  - Blind spot: Nie wiadomo, czy Coolify daje miejsce na hook przed rollbackiem.
- **Decision**: FIXED — Fix A, następnie rozszerzony, z jedną korektą po drodze.
  1. Wklejka zapisuje `string.Empty` zamiast `null` (`af5b636`).
  2. Skoro nic już nie zapisuje `null`, zdejmowanie `NOT NULL` przestało cokolwiek kupować. Pierwsza próba przepisała `AddSourceMaterialKind` w miejscu (`d1d298f`), **na błędnym założeniu, że migracja nie została nigdzie zaaplikowana**. Została — baza miała ją w `__EFMigrationsHistory`, `original_file_name` było już nullowalne i istniał jeden wiersz wklejki z `NULL`. `has-pending-model-changes` tego nie wykryło, bo porównuje model ze snapshotem, a nie ze schematem.
  3. Korekta: `AddSourceMaterialKind` przywrócony do treści, która faktycznie poszła na bazę, a ograniczenie wraca osobną migracją `RestoreOriginalFileNameNotNull` (`20260827145532`) — backfill `NULL` → `''`, potem `SET NOT NULL`, bez trwałego `DEFAULT`. Zweryfikowane na bazie po zastosowaniu: `is_nullable = NO`, zero `NULL`-i, RLS nietknięte.
  
  Wniosek na przyszłość: przed jakąkolwiek edycją istniejącej migracji sprawdź `__EFMigrationsHistory` w bazie, a nie `has-pending-model-changes`.

### F2 — Zbyt wąski catch gubi wklejony materiał przy wygasłej sesji

- **Severity**: WARNING
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Safety & Quality
- **Location**: `Components/Pages/Materials/Import.razor:191`
- **Detail**: Ścieżka zapisu łapie wyłącznie `DbUpdateException`. `AppDbContext.StampOwners` rzuca `InvalidOperationException("Cannot persist a user-owned entity without an authenticated user.")` (`Data/AppDbContext.cs:282-284`), gdy sesja wygaśnie w trakcie wypełniania strony — to nie jest `DbUpdateException`, więc wyjątek ucieka z handlera, zrywa obwód i użytkownik traci do 128 K znaków. Dokładnie ten kształt awarii, przed którym ten plasterek ma chronić (`HubWireLimits.cs:15-19`). Wzorzec odziedziczony po imporcie pliku, ale tam był tani — plik zostaje na dysku; wklejka nie istnieje nigdzie indziej.
- **Fix**: Dołożyć `catch (InvalidOperationException)` obok istniejącego, mapując oba na ten sam polski komunikat.
- **Decision**: FIXED

### F3 — Granica huba i jej test wyliczone wobec JSON, a hub używa blazorpack

- **Severity**: WARNING
- **Impact**: MEDIUM — realny kompromis; zatrzymaj się i przemyśl
- **Dimension**: Architecture
- **Location**: `Hosting/HubWireLimits.cs:40,59-62`; `tests/10xNotes.Tests/NoteWireLimitTests.cs:38,57,71,88`
- **Detail**: `WorstCaseBytesPerChar = 6` jest uzasadnione escapowaniem `\uXXXX` w JSON, ale komponenty Blazora negocjują `blazorpack` (MessagePack, surowy UTF-8, bez ekspansji escape). Rzeczywisty sufit to 3 bajty na jednostkę UTF-16. Stała ustawia więc `MaximumReceiveMessageSize = 851968` tam, gdzie `458752` niesie ten sam tekst — każdy obwód może przypiąć ~832 KB zbuforowanej ramki. Błąd jest w bezpieczną stronę, nic się nie psuje. Gorsze jest to, że cztery asercje w teście mierzą `Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(...))`, czyli protokół, którego ta ścieżka nie używa — test przypina arytmetykę do JSON zamiast do transportu, więc nie wykryłby realnego niedoszacowania i wywaliłby się na kimś, kto poprawi mnożnik.
- **Fix A (Recommended)**: Zmierzyć przez `IHubProtocol` rozwiązany jako `blazorpack` (`GetMessageBytes` na `InvocationMessage`), a mnożnik ustawić na 3 z komentarzem nazywającym protokół.
  - Strength: Test zaczyna mierzyć to, co faktycznie przechodzi przez drut — czyli spełnia regułę z `lessons.md` w duchu, nie tylko w literze. Zmniejsza ekspozycję pamięciową na obwód o połowę.
  - Tradeoff: Wiąże test z wewnętrznym szczegółem Blazora; zmiana protokołu w przyszłej wersji frameworka wymusi rewizję.
  - Confidence: MEDIUM — kierunek jest pewny, ale API `IHubProtocol` nie było w tym repo używane.
  - Blind spot: Nie zmierzono realnej ramki blazorpack dla 128 K znaków polskiego tekstu.
- **Fix B**: Zostawić 6 jako świadomy zapas i dopisać komentarz, że to celowe nadmiarowe zabezpieczenie, nie wyliczenie protokołu.
  - Strength: Zero ryzyka regresji; nadmiar chroni przed przyszłą zmianą protokołu.
  - Tradeoff: Test nadal mierzy nie ten protokół, więc reguła "pin the relationship in a test" pozostaje spełniona pozornie.
  - Confidence: HIGH — nic nie trzeba zmieniać.
  - Blind spot: Utrwala liczbę, której nikt później nie umie uzasadnić.
- **Decision**: FIXED

### F4 — Licznik znaków i komunikat o limicie nie robią tego, co obiecuje plan

- **Severity**: WARNING
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Success Criteria
- **Location**: `Components/Pages/Materials/Import.razor:74,318`
- **Detail**: Dwie drobne rozbieżności. (1) Licznik czyta `pasteContent.Length`, a `pasteContent` aktualizuje się dopiero na `@onchange`, czyli na blur — podczas pisania stoi na `0 / 131072` i skacze po wyjściu z pola. Manualny krok 2 planu (`plan.md:302`) brzmi "licznik znaków rośnie" i w tym brzmieniu nie przejdzie, mimo że zachowanie kodu jest świadome i zgodne z `NoteEditor`. (2) Zarówno licznik, jak i komunikat `TooLong` renderują surowe `131072` — bez separatora tysięcy i bez `PolishCulture`, której ten sam projekt używa do dat.
- **Fix**: Przeformułować krok 2 planu na to, co kod faktycznie robi (licznik aktualizuje się po opuszczeniu pola), i sformatować liczbę przez `PolishCulture` w obu miejscach.
- **Decision**: FIXED

### F5 — Nieaktualne odwołanie do przemianowanego typu

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Pattern Consistency
- **Location**: `Components/Notes/NoteEditor.razor:43`
- **Detail**: Komentarz mówi "NoteWireLimits is the other half of that guarantee", a plasterek przemianował ten typ na `HubWireLimits` i przeniósł do `Hosting/`. `HubWireLimits.cs:21-27` wprost argumentuje, że ten wskaźnik ma znaczenie — i jedyny komentarz w aplikacji, który na niego wskazuje, nie został zaktualizowany. `Import.razor:64` mówi poprawnie. Klasa testowa nadal nazywa się `NoteWireLimitTests`.
- **Fix**: Zaktualizować komentarz w `NoteEditor.razor`; opcjonalnie przemianować klasę testową.
- **Decision**: FIXED

### F6 — Wspólny helper tekstowy wylądował w cudzej przestrzeni nazw

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Architecture
- **Location**: `SourceMaterials/TextLimits.cs:13`, `Notes/NoteValidator.cs:1`
- **Detail**: `Truncate` bezpieczne wobec par zastępczych zostało wyciągnięte do przestrzeni `SourceMaterials`, więc `Notes/NoteValidator.cs` bierze `using _10xnotes.SourceMaterials;` po generyczny helper stringowy. To dokładnie to rozumowanie, którym ten sam plasterek uzasadnił przeniesienie `NoteWireLimits` **poza** `Notes/` do neutralnego `Hosting/` — zastosowane do jednego typu współdzielonego, a nie do drugiego. Plan wskazywał tę lokalizację, więc to nie jest drift.
- **Fix**: Neutralny dom (np. `Text/TextLimits.cs`) uspójniłby obie decyzje.
- **Decision**: FIXED

### F7 — Test pilnujący jednostek sam je myli

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Safety & Quality
- **Location**: `tests/10xNotes.Tests/NoteWireLimitTests.cs:109,115`
- **Detail**: `SignalRs_default_would_not_carry_a_full_note_or_paste` porównuje limit **bajtowy** z liczbą **znaków** (`@default < NoteValidator.MaxContentLength`). Asercja przechodzi i jest odziedziczona z S-01c — asercja wklejki tylko powiela kształt. To jednak dokładnie ta konfuzja jednostek, przed którą ostrzegają plan i `lessons.md`, siedząca wewnątrz testu istniejącego po to, by przed nią chronić.
- **Fix**: Porównywać z `MaxContentLength * WorstCaseBytesPerChar`, żeby asercja mówiła to, co znaczy.
- **Decision**: FIXED

### F8 — Zmiana kopii poza kontraktem planu

- **Severity**: OBSERVATION
- **Impact**: LOW — szybka decyzja; poprawka oczywista i wąska
- **Dimension**: Scope Discipline
- **Location**: `Components/Pages/Notes/Detail.razor:30`
- **Detail**: "Zaimportuj nowy materiał" → "Dodaj nowy materiał". Plan wymieniał tylko `Import.razor`, `NavMenu.razor` i `Materials/Detail.razor`. Link prowadzi do tej samej przemianowanej strony, więc pominięcie zostawiłoby żywą niespójność. Bez zmiany zachowania.
- **Fix**: Odnotować w planie jako addendum; nie cofać.
- **Decision**: FIXED

### F9 — Limit wklejki nie ma sprawdzenia wobec ostatniej warstwy pod nim

- **Severity**: OBSERVATION
- **Impact**: MEDIUM — realny kompromis; zatrzymaj się i przemyśl
- **Dimension**: Safety & Quality
- **Location**: `SourceMaterials/PasteValidator.cs:34`
- **Detail**: Limit wklejki to 128 K jednostek UTF-16, czyli dla polskiego tekstu ~256 KB i do ~384 KB UTF-8 — 2-3× więcej bajtów niż `MarkdownImportValidator.MaxFileSizeBytes`, którego własna dokumentacja (`MarkdownImportValidator.cs:22-25`) uzasadnia wartość tym, że utrzymuje dokument w oknie kontekstu modelu. Wklejka przechodzi przez tę samą warstwę bez odpowiadającego sprawdzenia ani testu. Degraduje się łagodnie (`NoteGenerator` klasyfikuje 413 i 400 kontekstowe jako `TooLong`), ale `GenerationQuotaService.TryReserveAsync` rezerwuje slot **przed** wywołaniem dostawcy — więc za długi materiał spala jedną z 50 dziennych generacji na nic. Domyślny model ma zapas kontekstu, więc bije to dopiero po zmianie `Ai__Model`.
- **Fix**: Dopisać zdanie przy `PasteValidator.MaxContentLength` nazywające model LLM jako ostatnią warstwę pod tym limitem, żeby przyszła zamiana modelu miała co sprawdzić.
- **Decision**: FIXED
