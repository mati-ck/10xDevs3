---
project: "10xNotes"
version: 1
status: draft
created: 2026-07-02
updated: 2026-07-02
prd_version: 1
main_goal: speed
top_blocker: capacity
---

# Roadmap: 10xNotes

> Derived from `context/foundation/prd.md` (v1) + auto-researched codebase baseline (2026-07-02).
> Edit-in-place; archive when superseded.
> Slices below are listed in dependency order. The "At a glance" table is the index.

## Vision recap

Przeglądanie długich materiałów odbija się o barierę startu: trzeba usiąść i przerobić obszerny tekst, więc zgromadzona wiedza leży nietknięta. 10xNotes usuwa tę barierę, generując z przesłanego materiału wstępny szkic notatki — człowiek już tylko go poprawia i zatwierdza, co przesuwa wysiłek z „napisz" na „popraw". Rdzeń produktu to zatem pętla: materiał źródłowy → wygenerowana przez AI notatka → akceptacja lub poprawa, mierzona kryterium 75% akceptacji notatek AI.

## North star

**S-01: Użytkownik importuje plik Markdown, generuje notatkę AI obok źródła, poprawia ją i zapisuje.** — to najmniejszy przepływ, którego skuteczne dostarczenie udowadnia główną hipotezę produktu (AI robi akceptowalny szkic z cudzego materiału), więc trafia najwcześniej, jak pozwalają jego zależności.

> „Gwiazda przewodnia" (north star) = najmniejszy przepływ end-to-end, którego udana premiera dowodzi, że produkt w ogóle działa — ustawiony tak wcześnie, jak pozwalają zależności, bo reszta roadmapy ma sens tylko wtedy, gdy ten fragment się broni. Tu jest to pętla „import → generowanie → akceptacja", bo dokładnie ona odpowiada na pytanie z Kryteriów sukcesu: czy 75% notatek AI jest akceptowanych.

## At a glance

| ID    | Change ID                  | Outcome (user can …)                                              | Prerequisites | PRD refs                        | Status   |
| ----- | -------------------------- | ---------------------------------------------------------------- | ------------- | ------------------------------- | -------- |
| F-01  | persistence-baseline       | (foundation) trwałe dane per użytkownik (DB + EF + migracje)      | —             | NFR: trwałość, NFR: prywatność  | ready    |
| F-02  | email-password-auth        | (foundation) rejestracja/logowanie e-mail+hasło, ochrona tras     | F-01          | FR-001, FR-002, Access Control  | proposed |
| S-01  | markdown-import-generation | zaimportować plik MD, wygenerować notatkę AI, poprawić i zapisać  | F-01, F-02    | US-01, FR-004, FR-005, FR-006, FR-008 | proposed |
| S-02  | paste-text-generation      | wkleić tekst jako źródło i wygenerować z niego notatkę            | S-01          | FR-003                          | proposed |
| S-03  | browse-notes-and-sources   | przeglądać własne notatki i materiały źródłowe                    | S-01, F-02    | FR-007                          | proposed |
| S-04  | edit-source-material       | edytować zapisany materiał źródłowy                               | S-01          | FR-009                          | blocked  |
| S-05  | delete-note                | usunąć własną notatkę                                             | S-01          | FR-010                          | proposed |
| S-06  | delete-source-material     | usunąć materiał źródłowy                                          | S-01          | FR-011                          | blocked  |

## Streams

Navigation aid — grupuje elementy dzielące łańcuch zależności. Kanoniczna kolejność żyje w grafie zależności poniżej; ta tabela to proponowana kolejność czytania w poprzek równoległych torów.

| Stream | Theme                              | Chain                                   | Note                                                                          |
| ------ | ---------------------------------- | --------------------------------------- | ----------------------------------------------------------------------------- |
| A      | Fundamenty i rdzeń (gwiazda)       | `F-01` → `F-02` → `S-01`                | Ścisła ścieżka must-have do walidacji; wszystko inne zależy od `S-01` (cel: speed). |
| B      | Warianty importu                   | `S-02`                                  | Dołącza do Strumienia A w `S-01`; drugie wejście (wklejanie tekstu).           |
| C      | Przeglądanie i cykl życia treści   | `S-03` / `S-05` (gotowe) · `S-04` / `S-06` (czekają) | Wszystkie odgałęziają od `S-01` i biegną równolegle; `S-04`/`S-06` blokują otwarte pytania. |

## Baseline

Co jest już w kodzie na dzień `2026-07-02` (auto-research + potwierdzone przez użytkownika).
Fundamenty poniżej zakładają obecność tych warstw i ich NIE odtwarzają.

- **Frontend:** present — Blazor Web App, Interactive Server render mode, Bootstrap 5.3; domyślne strony (Home/Counter/Weather), `Components/Layout/*`.
- **Backend / API:** present — host Blazor Server (`Program.cs`); brak własnych tras API; endpoint `/health` zarejestrowany.
- **Data:** absent — brak EF Core, `DbContext`, connection stringa i migracji.
- **Auth:** absent — brak pakietów Identity/auth i middleware; `tech-stack.md` deklaruje email+hasło (`has_auth: true`), lecz warstwa jest niewdrożona.
- **Deploy / infra:** present — `Dockerfile` multi-stage (non-root + HEALTHCHECK), `.github/workflows/deploy.yml`; cel wdrożenia: Coolify na VPS (`infrastructure.md`).
- **Observability:** partial — domyślne logowanie ASP.NET + endpoint `/health`; brak error-trackingu i metryk (nie wymuszane przez żadne NFR — nie zakładamy fundamentu).

## Foundations

### F-01: Trwała warstwa danych per użytkownik

- **Outcome:** (foundation) baza jest połączona (zewnętrzny zarządzany Postgres wg `infrastructure.md`), EF Core i mechanizm migracji działają, a rekordy da się przypisać i odpytać w zakresie jednego użytkownika. To minimalny kontrakt trwałości — nie pełny model danych.
- **Change ID:** persistence-baseline
- **PRD refs:** NFR (trwałość: dane przetrwają ponowne logowanie), NFR (prywatność: mechanizm izolacji per użytkownik)
- **Unlocks:** F-02 (magazyn tożsamości dla auth), S-01 (zapis notatki i materiału powiązanych z kontem)
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sekwencjonowany pierwszy, bo auth (F-02) i zapis notatek (S-01) nie istnieją bez magazynu; ryzyko to over-scope — wolno zbudować tylko połączenie + migracje + izolację, a nie encje domenowe (te wchodzą w S-01).
- **Status:** ready

### F-02: Uwierzytelnianie e-mail + hasło

- **Outcome:** (foundation) użytkownik może się zarejestrować, zalogować i wylogować; aplikacja rozpoznaje zalogowanego użytkownika i chroni trasy tak, że niezalogowany nie widzi żadnych danych. Płaski model, bez ról.
- **Change ID:** email-password-auth
- **PRD refs:** FR-001, FR-002, Access Control
- **Unlocks:** S-01 (zapis = akceptacja wiązana z kontem), S-03 (widok tylko własnych danych), egzekwuje NFR prywatności
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sekwencjonowany zaraz po trwałości, bo prywatność (NFR) blokuje premierę i gwiazda wymaga „zapisu na koncie"; ryzyko to rozrost w stronę pełnego systemu ról — trzymamy płaski model z PRD.
- **Status:** proposed

## Slices

### S-01: Import Markdown → generowanie AI → przegląd → zapis  *(gwiazda przewodnia)*

- **Outcome:** użytkownik importuje plik Markdown jako materiał źródłowy, jednym kliknięciem generuje obok niego notatkę AI, może ją poprawić i zapisać (zapis = akceptacja); materiał źródłowy pozostaje niezmieniony.
- **Change ID:** markdown-import-generation
- **PRD refs:** US-01, FR-004, FR-005, FR-006, FR-008, NFR (widoczna informacja zwrotna >2s)
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Najbardziej ryzykowne założenie produktu (jakość generowania AI domykająca 75% akceptacji) domyka się tutaj; wolno dowieźć minimalną pętlę, a nie od razu oba tryby importu (wklejanie idzie osobno w S-02), żeby przy budżecie „po godzinach" walidacja przyszła jak najszybciej.
- **Status:** proposed

### S-02: Wklejenie tekstu → generowanie AI

- **Outcome:** użytkownik wkleja surowy tekst jako materiał źródłowy i generuje z niego notatkę tą samą pętlą co w S-01.
- **Change ID:** paste-text-generation
- **PRD refs:** FR-003
- **Prerequisites:** S-01
- **Parallel with:** S-03, S-04, S-05, S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Drugie wejście do istniejącej pętli generowania — niskie ryzyko; jedyna pułapka to traktowanie wklejania jak osobnego przepływu zamiast wariantu wejścia S-01.
- **Status:** proposed

### S-03: Przeglądanie własnych notatek i materiałów

- **Outcome:** użytkownik widzi listę swoich zapisanych notatek i materiałów źródłowych i może otworzyć wybrany; nie widzi cudzych danych.
- **Change ID:** browse-notes-and-sources
- **PRD refs:** FR-007, NFR (prywatność)
- **Prerequisites:** S-01, F-02
- **Parallel with:** S-02, S-04, S-05, S-06
- **Blockers:** —
- **Unknowns:**
  - Czy materiał źródłowy ma być osobną listą najwyższego poziomu, czy dostępny głównie obok swojej notatki? — Owner: użytkownik. Block: no. (PRD zostawia to „do rozstrzygnięcia w designie" przy FR-007; nie blokuje planowania.)
- **Risk:** Sekwencjonowany po S-01, bo bez zapisanych notatek nie ma czego przeglądać; ryzyko to rozrost widoku w stronę wyszukiwania/filtrów spoza MVP.
- **Status:** proposed

### S-04: Edycja materiału źródłowego

- **Outcome:** użytkownik edytuje wcześniej zapisany materiał źródłowy.
- **Change ID:** edit-source-material
- **PRD refs:** FR-009
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-05, S-06
- **Blockers:** —
- **Unknowns:**
  - Czy materiał źródłowy jest edytowalny po wygenerowaniu notatki, a jeśli tak — co dzieje się z istniejącą notatką (pozostaje / oznaczana jako nieaktualna / regenerowana)? — Owner: użytkownik. Block: yes. (PRD Open Question 1.)
- **Risk:** Zachowanie edycji źródła wprost zależy od nierozstrzygniętego pytania o wierność notatki wobec zmienionego źródła; planowanie przed decyzją groziłoby przebudową.
- **Status:** blocked

### S-05: Usunięcie notatki

- **Outcome:** użytkownik usuwa jedną ze swoich notatek; materiał źródłowy pozostaje nietknięty.
- **Change ID:** delete-note
- **PRD refs:** FR-010
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-04, S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Prosta operacja na pojedynczej notatce, bez kaskady (to notatka jest usuwana, nie źródło) — najniższe ryzyko z całego cyklu życia treści.
- **Status:** proposed

### S-06: Usunięcie materiału źródłowego

- **Outcome:** użytkownik usuwa materiał źródłowy.
- **Change ID:** delete-source-material
- **PRD refs:** FR-011
- **Prerequisites:** S-01
- **Parallel with:** S-02, S-03, S-04, S-05
- **Blockers:** —
- **Unknowns:**
  - Co dzieje się z notatkami powiązanymi z materiałem źródłowym przy jego usunięciu (kaskadowe usunięcie vs osierocenie notatki)? — Owner: użytkownik. Block: yes. (PRD Open Question 2.)
- **Risk:** Zachowanie kaskady jest nierozstrzygnięte; zła domyślna decyzja (kaskada vs osierocenie) mogłaby nieodwracalnie usunąć zaakceptowane notatki, więc plasterek czeka na decyzję.
- **Status:** blocked

## Backlog Handoff

| Roadmap ID | Change ID                  | Suggested issue title                                   | Ready for `/10x-plan` | Notes                                  |
| ---------- | -------------------------- | ------------------------------------------------------- | --------------------- | -------------------------------------- |
| F-01       | persistence-baseline       | Trwała warstwa danych per użytkownik (DB + EF + migracje)| yes                   | Run `/10x-plan persistence-baseline`   |
| F-02       | email-password-auth        | Uwierzytelnianie e-mail + hasło + ochrona tras          | no                    | Czeka na F-01                          |
| S-01       | markdown-import-generation | Import MD → generowanie AI → przegląd → zapis (gwiazda)  | no                    | Czeka na F-01, F-02                     |
| S-02       | paste-text-generation      | Wklejenie tekstu jako źródła i generowanie notatki       | no                    | Czeka na S-01                          |
| S-03       | browse-notes-and-sources   | Przeglądanie własnych notatek i materiałów              | no                    | Czeka na S-01, F-02                     |
| S-04       | edit-source-material       | Edycja materiału źródłowego                             | no                    | Zablokowane — Open Question 1           |
| S-05       | delete-note                | Usunięcie notatki                                       | no                    | Czeka na S-01                          |
| S-06       | delete-source-material     | Usunięcie materiału źródłowego                          | no                    | Zablokowane — Open Question 2           |

## Open Roadmap Questions

1. **Czy materiał źródłowy jest edytowalny po wygenerowaniu notatki, a jeśli tak — co dzieje się z istniejącą notatką (pozostaje, oznaczana jako nieaktualna, regenerowana)?** — Owner: użytkownik. Block: S-04. (PRD Open Question 1, z rundy Sokratesa nad FR-009.)
2. **Co dzieje się z notatkami powiązanymi z materiałem źródłowym przy jego usunięciu (kaskadowe usunięcie vs osierocenie notatki)?** — Owner: użytkownik. Block: S-06. (PRD Open Question 2, z rundy Sokratesa nad FR-011.)

## Parked

- **Import innych formatów plików (PDF, .docx, audio, video)** — Why parked: PRD §Non-Goals; MVP przyjmuje tylko wklejony tekst i pliki Markdown, reszta wymaga osobnego parsowania (v2).
- **Współdzielenie notatek między użytkownikami** — Why parked: PRD §Non-Goals; produkt jednoosobowy, brak udostępniania utrzymuje płaski model dostępu.
- **Integracje z zewnętrznymi platformami/usługami** — Why parked: PRD §Non-Goals; żadnych konektorów w MVP, by nie wiązać zakresu z cudzymi API.
- **Aplikacja mobilna** — Why parked: PRD §Non-Goals; tylko web na start, natywny klient mobilny poza zakresem MVP.

## Done

(Empty on first generation. `/10x-archive` appends here — and flips the item's `Status` to `done` — when a change whose `Change ID` matches a roadmap item is archived. Do NOT pre-populate.)
