---
project: "10xNotes"
version: 1
status: draft
created: 2026-07-02
updated: 2026-09-08
prd_version: 2
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

Sam przepływ pozostaje niepodzielny jako **cel**, ale jest dostarczany w trzech kawałkach — `S-01a` (import) → `S-01b` (generowanie) → `S-01c` (przegląd i zapis). Gwiazda świeci dopiero po `S-01c`.

> „Gwiazda przewodnia" (north star) = najmniejszy przepływ end-to-end, którego udana premiera dowodzi, że produkt w ogóle działa — ustawiony tak wcześnie, jak pozwalają zależności, bo reszta roadmapy ma sens tylko wtedy, gdy ten fragment się broni. Tu jest to pętla „import → generowanie → akceptacja", bo dokładnie ona odpowiada na pytanie z Kryteriów sukcesu: czy 75% notatek AI jest akceptowanych.

## At a glance

| ID    | Change ID                  | Outcome (user can …)                                              | Prerequisites | PRD refs                        | Status   |
| ----- | -------------------------- | ---------------------------------------------------------------- | ------------- | ------------------------------- | -------- |
| F-01  | persistence-baseline       | (foundation) trwałe dane per użytkownik (DB + EF + migracje)      | —             | NFR: trwałość, NFR: prywatność  | done     |
| F-02  | email-password-auth        | (foundation) rejestracja/logowanie e-mail+hasło, ochrona tras     | F-01          | FR-001, FR-002, Access Control  | done     |
| F-03  | auth-session-hardening     | (foundation) rewalidacja sesji + kontrakt właściciela przy zapisie | F-01, F-02    | Access Control, NFR: prywatność | done     |
| S-01a | markdown-import            | zaimportować plik Markdown i mieć go zapisanym na koncie          | F-01, F-02    | FR-004                          | done     |
| S-01b | ai-note-generation         | wygenerować notatkę AI obok materiału (jeszcze bez zapisu)        | S-01a         | US-01 (część), FR-005           | done     |
| S-01c | note-review-save           | poprawić wygenerowaną notatkę i zapisać ją (zapis = akceptacja)   | S-01b         | US-01 (domknięcie), FR-006, FR-008 | done     |
| S-02  | paste-text-generation      | wkleić tekst jako źródło i wygenerować z niego notatkę            | S-01c         | FR-003                          | done         |
| S-03  | browse-notes-and-sources   | przeglądać własne notatki i materiały źródłowe                    | S-01c, F-02   | FR-007                          | done        |
| S-04  | edit-source-material       | edytować zapisany materiał źródłowy                               | S-01a         | FR-009                          | blocked  |
| S-05  | delete-note                | usunąć własną notatkę                                             | S-01c         | FR-010                          | proposed |
| S-06  | delete-source-material     | usunąć materiał źródłowy                                          | S-01a         | FR-011                          | blocked  |
| S-07  | user-profile               | zarządzać kontem: nazwa wyświetlana, hasło, usunięcie konta       | F-02          | FR-012, FR-013, FR-014          | planning |

> **S-01 (gwiazda przewodnia)** nie jest już pojedynczym plasterkiem — pakował cztery rzeczy naraz (model domenowy, import pliku, integrację z AI, edytor + zapis), a zależało od niego wszystko pozostałe. Rozbity 2026-08-13 na `S-01a` → `S-01b` → `S-01c`; gwiazda jest dowiedziona dopiero po `S-01c`. Wycofany change ID: `import-generation-review-save`.

## Streams

Navigation aid — grupuje elementy dzielące łańcuch zależności. Kanoniczna kolejność żyje w grafie zależności poniżej; ta tabela to proponowana kolejność czytania w poprzek równoległych torów.

| Stream | Theme                              | Chain                                   | Note                                                                          |
| ------ | ---------------------------------- | --------------------------------------- | ----------------------------------------------------------------------------- |
| A      | Fundamenty i rdzeń (gwiazda)       | `F-01` → `F-02` → `F-03` → `S-01a` → `S-01b` → `S-01c` | Ścisła ścieżka must-have do walidacji; wszystko inne zależy od jakiegoś kawałka `S-01` (cel: speed). `F-03` doszedł w trakcie — patrz jego sekcja. |
| B      | Warianty importu                   | `S-02`                                  | Dołącza do Strumienia A po `S-01c`; drugie wejście (wklejanie tekstu) do gotowej pętli. |
| C      | Przeglądanie i cykl życia treści   | `S-03` / `S-05` (po `S-01c`) · `S-04` / `S-06` (po `S-01a`, czekają na decyzje) | Odgałęziają od różnych kawałków `S-01` i biegną równolegle; `S-04`/`S-06` blokują otwarte pytania. Cykl życia materiału źródłowego zaczepia się już o `S-01a`, więc nie czeka na całą gwiazdę. |
| D      | Zarządzanie kontem                 | `S-07`                                  | Dołączony 2026-09-08. Odgałęzia od `F-02` i nie dotyka pętli generowania, więc biegnie równolegle do całej reszty. Osobny tor, bo żaden istniejący nie opisuje konta: A to fundamenty i gwiazda, B warianty wejścia, C cykl życia **treści** — konto to inny obiekt. Miejsce na przyszłe zmiany konta (reset hasła, zmiana e-maila). |

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
- **Unlocks:** F-02 (magazyn tożsamości dla auth), S-01a (zapis materiału źródłowego powiązanego z kontem)
- **Prerequisites:** —
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sekwencjonowany pierwszy, bo auth (F-02) i zapis notatek (S-01a/S-01c) nie istnieją bez magazynu; ryzyko to over-scope — wolno zbudować tylko połączenie + migracje + izolację, a nie encje domenowe (te wchodzą w S-01a i S-01c).
- **Status:** done

### F-02: Uwierzytelnianie e-mail + hasło

- **Outcome:** (foundation) użytkownik może się zarejestrować, zalogować i wylogować; aplikacja rozpoznaje zalogowanego użytkownika i chroni trasy tak, że niezalogowany nie widzi żadnych danych. Płaski model, bez ról.
- **Change ID:** email-password-auth
- **PRD refs:** FR-001, FR-002, Access Control
- **Unlocks:** S-01a (materiał wiązany z kontem), S-01c (zapis = akceptacja wiązana z kontem), S-03 (widok tylko własnych danych), egzekwuje NFR prywatności
- **Prerequisites:** F-01
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Sekwencjonowany zaraz po trwałości, bo prywatność (NFR) blokuje premierę i gwiazda wymaga „zapisu na koncie"; ryzyko to rozrost w stronę pełnego systemu ról — trzymamy płaski model z PRD.
- **Status:** done

### F-03: Domknięcie sesji i kontraktu właściciela

> **Dopisany do roadmapy 2026-09-08, wstecz.** Nie pochodzi z pierwotnej generacji — powstał 2026-08-02 z read-only przeglądu planu F-02 plus pozycji przeniesionych z przeglądu F-01, został zaplanowany, zaimplementowany i zarchiwizowany poza roadmapą. Issue [#22](https://github.com/mati-ck/10xDevs3/issues/22) istniał od początku; brakowało wyłącznie wpisu tutaj.

- **Outcome:** (foundation) stan sesji i kontrakt właściciela przy zapisie są domknięte, zanim S-01 zapisze pierwszą encję należącą do użytkownika: obwód SignalR rewaliduje tożsamość, rejestracja nie wystawia ciasteczka bez sesji z GoTrue, `OwnerId` jest tokenem współbieżności (więc `owner_id` wchodzi do `WHERE` przy `UPDATE`/`DELETE`), a strażnicy zapisu mają testy. Bez nowego zachowania widocznego dla użytkownika.
- **Change ID:** auth-session-hardening
- **PRD refs:** Access Control, NFR (prywatność)
- **Unlocks:** S-01a — zdejmuje dwa przeniesione ryzyka blokujące czysty start gwiazdy
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Fundament wstawiony między F-02 a S-01a właśnie dlatego, że jego koszt rośnie z każdą encją zapisaną pod starym kontraktem; ryzyko to rozrost w stronę pełnego hardeningu HTTP (rate limiting na logowaniu został świadomie poza zakresem).
- **Status:** done

## Slices

### S-01: Import Markdown → generowanie AI → przegląd → zapis  *(gwiazda przewodnia — parasol)*

Gwiazda przewodnia to nadal ta jedna pętla end-to-end i to ona odpowiada na pytanie z Kryteriów sukcesu (75% akceptacji). Nie jest jednak jednym plasterkiem: w pierwotnym kształcie łączyła model domenowy, import pliku, integrację z dostawcą AI i edytor z zapisem — a przy tym każdy inny plasterek (S-02…S-06) czekał na całość. Rozbita 2026-08-13 na trzy kawałki dostarczane po kolei; **hipoteza produktu jest dowiedziona dopiero po `S-01c`**, wcześniejsze kawałki są demonstrowalne, ale nie rozstrzygające. Change ID `import-generation-review-save` jest wycofany (folder zmiany nie powstał).

#### S-01a: Import pliku Markdown → zapisany materiał źródłowy

- **Outcome:** użytkownik wgrywa plik `.md` i widzi go zapisanego na swoim koncie — materiał przetrwa wylogowanie i nie jest widoczny dla nikogo innego. Bez generowania.
- **Change ID:** markdown-import
- **PRD refs:** FR-004, NFR (trwałość), NFR (prywatność)
- **Prerequisites:** F-01, F-02
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - Limit rozmiaru importowanego pliku i zachowanie po jego przekroczeniu? — Owner: użytkownik. Block: no. (PRD milczy; domyślnie rozsądny limit ustalony w planie.)
- **Risk:** Pierwsza encja domenowa w projekcie — ryzyko to wciągnięcie modelu notatki i relacji zanim generowanie w ogóle istnieje. Wolno dowieźć wyłącznie `SourceMaterial` + migrację z `ENABLE ROW LEVEL SECURITY` + upload; encja notatki należy do S-01c.
- **Status:** done

#### S-01b: Generowanie notatki AI obok materiału

- **Outcome:** jednym kliknięciem użytkownik generuje notatkę z zapisanego materiału i widzi ją obok źródła, z ciągłą, widoczną informacją zwrotną w trakcie. Notatka jest na tym etapie ulotna (nie trafia jeszcze na konto), materiał źródłowy pozostaje niezmieniony.
- **Change ID:** ai-note-generation
- **PRD refs:** US-01 (część), FR-005, Business Logic, NFR (widoczna informacja zwrotna >2s)
- **Prerequisites:** S-01a
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - ~~Który dostawca AI, jaki model i jak trzymany klucz?~~ — Rozstrzygnięte 2026-08-17 w `plan.md`: `Microsoft.Extensions.AI` (`IChatClient`) na endpoint OpenRouter, model `google/gemini-3.7-flash` jako wartość konfiguracji, klucz jako sekret (`Ai__ApiKey`).
- **Risk:** Tu domyka się najbardziej ryzykowne założenie produktu — jakość generowania. Odcięcie tego kawałka od zapisu jest celowe: prompt i dostawcę da się iterować bez dotykania modelu danych ani edytora. Ryzyko to rozrost w stronę parametrów generowania (PRD wymaga jednego kliknięcia, bez ustawień).
- **Status:** done

#### S-01c: Przegląd, edycja i zapis notatki (zapis = akceptacja)

- **Outcome:** użytkownik poprawia wygenerowaną notatkę i zapisuje ją — zapis wiąże notatkę z kontem i liczy się jako akceptacja; porzucenie bez zapisu nie pozostawia jej na koncie; materiał źródłowy zostaje nietknięty. Ten kawałek domyka gwiazdę.
- **Change ID:** note-review-save
- **PRD refs:** US-01 (domknięcie), FR-006, FR-008, NFR (trwałość), NFR (prywatność)
- **Prerequisites:** S-01b
- **Parallel with:** —
- **Blockers:** —
- **Unknowns:**
  - ~~Czy jeden materiał źródłowy może mieć wiele zapisanych notatek (1:N), czy kolejny zapis nadpisuje poprzednią?~~ — Rozstrzygnięte 2026-08-17 w `plan.md`: **ściśle 1:1**, kolejny zapis nadpisuje po potwierdzeniu. OQ2 pozostaje otwarte celowo — FK `notes → source_materials` jest ustawiony na `ON DELETE NO ACTION`, żeby domyślna kaskada EF nie przesądziła odpowiedzi za S-06.
- **Risk:** Dopiero po tym plasterku da się w ogóle mierzyć 75% akceptacji, więc to on decyduje o terminie walidacji; ryzyko to rozrost w stronę wersjonowania notatek i historii zmian (poza MVP).
- **Status:** done

### S-02: Wklejenie tekstu → generowanie AI

- **Outcome:** użytkownik wkleja surowy tekst jako materiał źródłowy i generuje z niego notatkę tą samą pętlą co w S-01a→S-01c.
- **Change ID:** paste-text-generation
- **PRD refs:** FR-003
- **Prerequisites:** S-01c (cała pętla musi istnieć, zanim doda się drugie wejście)
- **Parallel with:** S-03, S-04, S-05, S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Drugie wejście do istniejącej pętli generowania — niskie ryzyko; jedyna pułapka to traktowanie wklejania jak osobnego przepływu zamiast drugiego wariantu wejścia obok S-01a.
- **Status:** done

### S-03: Przeglądanie własnych notatek i materiałów

- **Outcome:** użytkownik widzi listę swoich zapisanych notatek i materiałów źródłowych i może otworzyć wybrany; nie widzi cudzych danych.
- **Change ID:** browse-notes-and-sources
- **PRD refs:** FR-007, NFR (prywatność)
- **Prerequisites:** S-01c, F-02
- **Parallel with:** S-02, S-04, S-05, S-06
- **Blockers:** —
- **Unknowns:**
  - ~~Czy materiał źródłowy ma być osobną listą najwyższego poziomu, czy dostępny głównie obok swojej notatki?~~ — Rozstrzygnięte 2026-08-18 w `change.md`: **dwie osobne strony**, `/` = notatki, `/materials` = materiały, linkowane krzyżowo w obie strony.
- **Risk:** Sekwencjonowany po S-01c, bo bez zapisanych notatek nie ma czego przeglądać; ryzyko to rozrost widoku w stronę wyszukiwania/filtrów spoza MVP.
- **Status:** done

### S-04: Edycja materiału źródłowego

- **Outcome:** użytkownik edytuje wcześniej zapisany materiał źródłowy.
- **Change ID:** edit-source-material
- **PRD refs:** FR-009
- **Prerequisites:** S-01a (wystarczy zapisany materiał źródłowy — nie czeka na resztę gwiazdy)
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
- **Prerequisites:** S-01c (bez zapisanych notatek nie ma czego usuwać)
- **Parallel with:** S-02, S-03, S-04, S-06
- **Blockers:** —
- **Unknowns:** —
- **Risk:** Prosta operacja na pojedynczej notatce, bez kaskady (to notatka jest usuwana, nie źródło) — najniższe ryzyko z całego cyklu życia treści.
- **Status:** proposed

### S-06: Usunięcie materiału źródłowego

- **Outcome:** użytkownik usuwa materiał źródłowy.
- **Change ID:** delete-source-material
- **PRD refs:** FR-011
- **Prerequisites:** S-01a (wystarczy zapisany materiał źródłowy — nie czeka na resztę gwiazdy)
- **Parallel with:** S-02, S-03, S-04, S-05
- **Blockers:** —
- **Unknowns:**
  - Co dzieje się z notatkami powiązanymi z materiałem źródłowym przy jego usunięciu (kaskadowe usunięcie vs osierocenie notatki)? — Owner: użytkownik. Block: yes. (PRD Open Question 2.)
- **Risk:** Zachowanie kaskady jest nierozstrzygnięte; zła domyślna decyzja (kaskada vs osierocenie) mogłaby nieodwracalnie usunąć zaakceptowane notatki, więc plasterek czeka na decyzję.
- **Status:** blocked

### S-07: Zarządzanie kontem

> **Dopisany do roadmapy 2026-09-08.** Nie pochodzi z pierwotnej generacji — powstał z decyzji produktowej podjętej przy planowaniu, a odpowiadające mu FR-012…FR-014 zostały dopisane do PRD (v2) w tej samej rundzie. Kolejność jest zatem odwrotna niż u pozostałych plasterków: tam PRD poprzedzał roadmapę, tu roadmapa i plan wymusiły uzupełnienie PRD.

- **Outcome:** użytkownik wchodzi na `/profile` i zarządza swoim kontem: ustawia albo czyści nazwę wyświetlaną (która zastępuje e-mail w nawigacji), zmienia hasło podając dotychczasowe, oraz usuwa konto wraz ze wszystkimi swoimi danymi — potwierdzając to hasłem.
- **Change ID:** user-profile
- **PRD refs:** FR-012, FR-013, FR-014, Access Control
- **Prerequisites:** F-02 (bez kont nie ma czym zarządzać), F-03 (kontrakt właściciela i kaskady, na których opiera się usunięcie konta)
- **Parallel with:** S-02, S-03, S-04, S-05, S-06 — nie dotyka pętli generowania ani modelu treści
- **Blockers:** —
- **Unknowns:**
  - ~~Czy usunięcie konta wymaga klucza `service_role`?~~ — Rozstrzygnięte 2026-09-08 w `plan.md`: **nie**. Zweryfikowano na projekcie, że rola `postgres` ma `DELETE` na `auth.users`, więc kasowanie idzie po istniejącym połączeniu, a kaskady FK robią resztę.
  - ~~Jak zmienić hasło, skoro `AuthCookie` świadomie porzuca tokeny GoTrue?~~ — Rozstrzygnięte 2026-09-08 w `plan.md`: **ponowne uwierzytelnienie** dotychczasowym hasłem; token żyje wyłącznie wewnątrz jednej metody, więc ciasteczko pozostaje jedynym pojęciem sesji.
- **Risk:** Jedyny plasterek z operacją nieodwracalną — usunięcie konta kasuje kaskadowo pięć tabel, w tym `note_events`, czyli wkład tego konta w metrykę 75% akceptacji. Kaskady nie da się dowieść zestawem testów (żyje w kluczach obcych do `auth.users`, a harness SQLite nie ma takiej tabeli), więc dowodem jest weryfikacja ręczna na koncie jednorazowym. Drugie ryzyko to rozrost w stronę pełnego zarządzania kontem — zmiana e-maila, reset hasła dla wylogowanego i unieważnianie sesji są świadomie poza zakresem.
- **Status:** planning

## Backlog Handoff

> **Migawka z 2026-07-02, nieaktualizowana.** Kolumna „Ready for `/10x-plan`" opisuje stan w dniu przekazania backlogu do GitHuba i nie odzwierciedla dzisiejszego postępu — nie ma tu też F-03 ani rozbicia S-01. Żywy status trzymają: kolumna Status w „At a glance", sekcja `## Done` i GitHub Issues (mapowanie w `task-github.md`).

| Roadmap ID | Change ID                  | Suggested issue title                                   | Ready for `/10x-plan` | Notes                                  |
| ---------- | -------------------------- | ------------------------------------------------------- | --------------------- | -------------------------------------- |
| F-01       | persistence-baseline       | Trwała warstwa danych per użytkownik (DB + EF + migracje)| yes                   | Run `/10x-plan persistence-baseline`   |
| F-02       | email-password-auth        | Uwierzytelnianie e-mail + hasło + ochrona tras          | no                    | Czeka na F-01                          |
| S-01a      | markdown-import            | Import pliku Markdown jako materiał źródłowy (gwiazda 1/3)| yes                   | Run `/10x-plan markdown-import`        |
| S-01b      | ai-note-generation         | Generowanie notatki AI obok materiału (gwiazda 2/3)      | no                    | Czeka na S-01a; wybór dostawcy AI domyka research w planie |
| S-01c      | note-review-save           | Przegląd, edycja i zapis notatki (gwiazda 3/3)           | no                    | Czeka na S-01b                          |
| S-02       | paste-text-generation      | Wklejenie tekstu jako źródła i generowanie notatki       | no                    | Czeka na S-01c                         |
| S-03       | browse-notes-and-sources   | Przeglądanie własnych notatek i materiałów              | no                    | Czeka na S-01c, F-02                    |
| S-04       | edit-source-material       | Edycja materiału źródłowego                             | no                    | Zablokowane — Open Question 1           |
| S-05       | delete-note                | Usunięcie notatki                                       | no                    | Czeka na S-01c                         |
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

- **F-01: (foundation) baza jest połączona, EF Core i migracje działają, a rekordy da się przypisać i odpytać w zakresie jednego użytkownika.** — Archived 2026-09-08 → `context/archive/2026-07-27-persistence-baseline/`. Issue [#6](https://github.com/mati-ck/10xDevs3/issues/6) closed 2026-07-27; status w tabeli był przerzucony ręcznie 2026-08-13, folder doarchiwizowany dopiero przy porządkach 2026-09-08. Lesson: —.
- **F-02: (foundation) użytkownik może się zarejestrować, zalogować i wylogować; aplikacja rozpoznaje zalogowanego użytkownika i chroni trasy tak, że niezalogowany nie widzi żadnych danych. Płaski model, bez ról.** — Archived 2026-08-02 → `context/archive/2026-07-27-email-password-auth/`. Lesson: —.
- **F-03: (foundation) stan sesji i kontrakt właściciela przy zapisie są domknięte, zanim S-01 zapisze pierwszą encję należącą do użytkownika. Bez nowego zachowania widocznego dla użytkownika.** — Archived 2026-08-02 → `context/archive/2026-08-02-auth-session-hardening/`. Wpis dopisany 2026-09-08 wstecz: `/10x-archive` go nie napisał, bo pozycji F-03 nie było wtedy w roadmapie. Lesson: —.
- **S-01a: użytkownik wgrywa plik `.md` i widzi go zapisanego na swoim koncie — materiał przetrwa wylogowanie i nie jest widoczny dla nikogo innego. Bez generowania.** — Archived 2026-08-17 → `context/archive/2026-08-17-markdown-import/`. Lesson: —.
- **S-01b: jednym kliknięciem użytkownik generuje notatkę z zapisanego materiału i widzi ją obok źródła, z ciągłą, widoczną informacją zwrotną w trakcie. Notatka jest na tym etapie ulotna (nie trafia jeszcze na konto), materiał źródłowy pozostaje niezmieniony.** — Archived 2026-08-17 → `context/archive/2026-08-17-ai-note-generation/`. Lesson: —.
- **S-01c: użytkownik poprawia wygenerowaną notatkę i zapisuje ją — zapis wiąże notatkę z kontem i liczy się jako akceptacja; porzucenie bez zapisu nie pozostawia jej na koncie; materiał źródłowy zostaje nietknięty. Ten kawałek domyka gwiazdę.** — Archived 2026-08-17 → `context/archive/2026-08-17-note-review-save/`. Lesson: „An advertised limit must be one every layer beneath it can carry" (`lessons.md`).
- **S-02: użytkownik wkleja surowy tekst jako materiał źródłowy i generuje z niego notatkę tą samą pętlą co w S-01a→S-01c.** — Archived 2026-08-27 → `context/archive/2026-08-18-paste-text-generation/`. Lesson: —.
- **S-03: użytkownik widzi listę swoich zapisanych notatek i materiałów źródłowych i może otworzyć wybrany; nie widzi cudzych danych.** — Archived 2026-09-08 → `context/archive/2026-08-18-browse-notes-and-sources/`. Weryfikacja ręczna domknięta tego samego dnia — 2.10 (przesunięcie na górę listy) i 2.12 (izolacja drugiego konta) dowiedzione dopiero teraz. Lesson: —.
