# Przegląd, edycja i zapis notatki (S-01c) — Plan Brief

> Full plan: `context/changes/note-review-save/plan.md`

## What & Why

Użytkownik poprawia wygenerowaną notatkę i zapisuje ją — zapis wiąże ją z kontem i **liczy się jako akceptacja**, porzucenie bez zapisu nie zostawia niczego. To trzeci i ostatni kawałek gwiazdy przewodniej: dopiero po nim pętla „materiał → notatka AI → akceptacja" jest zamknięta, a kryterium 75% akceptacji z PRD w ogóle mierzalne.

## Starting Point

Po S-01b `/materials/{id}` jest dwukolumnowym workspace'em: materiał po lewej, strumieniowana notatka po prawej. Notatka żyje wyłącznie w polu `string note` (`Detail.razor:105`) i ginie przy odświeżeniu. Nie ma encji notatki, żadnego zapisu, żadnego edytora ani renderowania Markdownu — panel pokazuje `<pre>`, więc użytkownik czyta dosłowne `##` i `-`. `NotePrompt.Version = "v2"` czeka z komentarzem „not persisted yet; ephemeral until S-01c".

## Desired End State

Po zakończeniu strumienia panel zamienia się w edytor: tytuł wypełniony z materiału, textarea ze źródłem Markdown, przełącznik **Podgląd** pokazujący notatkę jako strukturę. Zapis (z pytaniem o zastąpienie, jeśli notatka już istnieje) prowadzi na `/notes/{id}` — znowu dwie kolumny, materiał obok notatki, dalej edytowalnej i zapisywalnej w miejscu. Materiał źródłowy jest nietknięty w każdym przepływie.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Relacja materiał↔notatka (otwarte pytanie roadmapy) | Ściśle 1:1, zapis nadpisuje | Najprostszy model i UI; koszt — zniszczenie wcześniej zaakceptowanej pracy — kupiony z powrotem potwierdzeniem | Plan |
| Co zapisujemy | Szkic AI + tekst finalny + wersja promptu i model | Zamienia „75%" z odpowiedzi tak/nie w mierzalne „ile człowiek musiał poprawić" | Plan |
| Tytuł notatki | Wyprowadzony z materiału, poprawialny | Kopia wzorca importu: nigdy nie blokuje, nigdy nie zostawia złej nazwy | Plan |
| Edycja i prezentacja (odziedziczone F9) | Textarea + podgląd przez Markdig z wyłączonym HTML-em | Użytkownik ocenia notatkę czytając strukturę, a nie surowe `##` — a to jego ocena jest mierzona | Plan |
| Zapis nad istniejącą notatką | Potwierdzenie przed zastąpieniem | Jedyny koszt 1:1 to skasowana zaakceptowana praca; odkupiony jednym kliknięciem | Plan |
| Po zapisie | Nawigacja na `/notes/{id}` | Notatka dostaje własny adres, gotowy pod S-03 i S-05 | Plan |
| Niezapisane zmiany | Chronione tylko przed ponownym generowaniem | Chroni destrukcyjną akcję na tej samej stronie; bez `NavigationLock` | Plan |
| Edycja w trakcie strumienia | Panel tylko do odczytu do końca generowania | Dokładnie jeden właściciel bufora — ta strona ma udokumentowaną historię wyścigów (F1, F3) | Plan |
| Pomiar 75% | Dopisywalny wyłącznie rejestr zdarzeń | Przy nadpisywaniu 1:1 tabela notatek nie potrafi policzyć akceptacji, które już się zdarzyły | Plan |
| Ponowna edycja zapisanej notatki | Tak, na `/notes/{id}`, zapis w miejscu | Bez tego jedyną drogą do poprawki jest regeneracja, która pod 1:1 kasuje poprawianą notatkę | Plan |
| Gdzie mieszka logika | Czysty szew + serwis; strony cienkie | Brak bUnit — cokolwiek ma być udowodnione, musi żyć poza komponentem | Plan |

## Scope

**In scope:** pakiet Markdig, `NoteValidator` i `NoteMarkdown` (renderowanie z wyłączonym HTML-em + allowlista schematów URL), encje `Note` i `NoteEvent` z migracją (FK + RLS), `NoteService` z rozdzielonym `AcceptAsync`/`UpdateAsync`, współdzielony komponent `NoteEditor`, zapis z potwierdzeniem na stronie materiału, nowa strona `/notes/{id}` z ponowną edycją.

**Out of scope:** wiele notatek na materiał, lista notatek (S-03), usuwanie notatki (S-05), edycja materiału (S-04), wklejanie tekstu (S-02), rozstrzygnięcie OQ2, ochrona przy nawigacji (`NavigationLock`), autozapis wersji roboczej, UI do odczytu rejestru, osobny pakiet sanitizera HTML.

## Architecture / Approach

`Detail.razor` (materiał) i `Notes/Detail.razor` (notatka) → współdzielony `NoteEditor` → `NoteService` (przez `UserScopedDbContextFactory`) → `notes` + `note_events`. Renderowanie idzie przez czysty `NoteMarkdown.ToHtml`, jedyne miejsce w projekcie, gdzie treść staje się `MarkupString`. Strumieniowanie z S-01b zostaje nietknięte; edytor przejmuje bufor **raz**, po zakończeniu strumienia. Serwis i szew zwracają typowane wyniki, strony mapują je na polską kopię — dokładnie jak `MarkdownImportValidator` → `Import.razor`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Szew notatki (czysty) | Walidacja + renderowanie Markdownu, testowane bez bazy i przeglądarki | `DisableHtml()` nie jest sanitizerem — allowlista URL-i jest wymagana, nie opcjonalna |
| 2. Trwałość notatki | Dwie tabele z RLS, serwis zapisujący i rejestrujący | FK do `source_materials` łatwo domyślnie ustawić na `Cascade` i po cichu rozstrzygnąć OQ2 |
| 3. Edytor i zapis | Edytor z podglądem, zapis z potwierdzeniem, nawigacja na notatkę | Dołożenie edytowalnego bufora na bufor strumieniowy na stronie z historią wyścigów |
| 4. Strona notatki | `/notes/{id}` z ponowną edycją i zapisem w miejscu | Ponowny zapis nie może dopisać zdarzenia akceptacji, inaczej wskaźnik przekroczy 100% |

**Prerequisites:** S-01b wdrożone (jest); klucz `Ai__ApiKey` skonfigurowany do ręcznych testów generowania.
**Estimated effort:** ~4 sesje, po jednej na fazę.

## Open Risks & Assumptions

- **Renderowanie bez osobnego sanitizera.** `DisableHtml()` + allowlista schematów zamyka powierzchnię, o której wie dokumentacja Markdiga, ale to argument, nie dowód — jeśli przyszła wersja Markdiga zacznie emitować atrybut pod kontrolą treści, którego nie przewidzieliśmy, założenie pęka. Łagodzi to fakt, że notatka jest widoczna wyłącznie swojemu właścicielowi (RLS + filtry właściciela + brak współdzielenia w PRD), więc powierzchnia jest samo-XSS, nie cudza.
- **1:1 kasuje zaakceptowaną pracę.** Potwierdzenie chroni przed przypadkiem, ale nie ma cofnięcia nigdzie w produkcie; użytkownik, który potwierdzi zastąpienie, traci poprzednią notatkę bezpowrotnie.
- **Niezapisane zmiany giną przy wyjściu ze strony** — decyzja świadoma; najczęstsza droga utraty pracy zostaje niechroniona.
- **Rejestr akceptacji nikt nie czyta z poziomu aplikacji.** Wskaźnik trzeba policzyć zapytaniem do bazy; jeśli nikt tego nie zrobi, koszt dwóch kolumn i tabeli nie zwróci się.
- **Komponent `NoteEditor` pozostaje nieprzetestowany** — brak bUnit jest ograniczeniem projektu, a przełącznik podglądu i przepływ potwierdzeń są weryfikowane wyłącznie ręcznie.
- **`UpdatedAt` to pierwsza taka kolumna w projekcie** — jeśli inne encje kiedyś jej potrzebują, warto ujednolicić, zanim wzorzec się rozjedzie.

## Success Criteria (Summary)

- Użytkownik poprawia wygenerowaną notatkę i zapisuje ją; notatka przeżywa odświeżenie i ponowne zalogowanie, a materiał źródłowy zostaje nietknięty.
- Notatka jest czytana jako struktura (nagłówki, punkty), nie jako surowy Markdown — i żadna treść, ani od modelu, ani wpisana ręcznie, nie potrafi wykonać kodu w podglądzie.
- Porzucenie bez zapisu nie zostawia niczego na koncie, a rejestr pozwala policzyć, jaki odsetek wygenerowanych notatek został zaakceptowany.
