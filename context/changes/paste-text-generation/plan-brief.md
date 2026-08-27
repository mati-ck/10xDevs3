# Wklejenie tekstu → generowanie notatki (S-02) — Plan Brief

> Pełny plan: `context/changes/paste-text-generation/plan.md`

## What & Why

Użytkownik może dziś dostarczyć materiał tylko jako plik `.md`. Ten plasterek dokłada drugie wejście do tej samej pętli: wklejony tekst staje się `SourceMaterial` i generuje się z niego notatka dokładnie tak jak z zaimportowanego pliku. To FR-003 — wklejanie jest, wg uzasadnienia w PRD, najniższym progiem wejścia do pierwszej notatki.

## Starting Point

Pętla import → generowanie → przegląd → zapis działa w całości (S-01a→S-01c, wszystkie zarchiwizowane). Od momentu zapisu wiersza nic w niej nie wie, skąd wziął się materiał: `Materials/Detail.razor:229` czyta wiersz po id, a `NotePrompt.BuildMessages` bierze tylko `Title` i `Content`. Brakuje trzech rzeczy: modelu, który potrafi opisać materiał bez pliku (`original_file_name` jest `NOT NULL`), reguł walidacji dla wklejonego stringa, i granicy transportu SignalR wyprowadzonej z czegoś więcej niż limit notatki.

## Desired End State

Na `/materials/import` widać przełącznik „Wklej tekst" / „Plik .md", domyślnie ustawiony na wklejanie. Użytkownik wkleja materiał, poprawia podpowiedziany z pierwszej linii tytuł, zapisuje i ląduje na `/materials/{id}` — gdzie „Generuj notatkę" działa identycznie jak dla pliku. Materiały zaimportowane wcześniej nadal pokazują swoją nazwę pliku.

## Key Decisions Made

| Decyzja | Wybór | Dlaczego | Źródło |
| --- | --- | --- | --- |
| Trasa | Jedna strona `/materials/import`, dwie zakładki | Roadmapa nazywa S-02 drugim wejściem, nie drugim przepływem; wspólny tytuł, zapis i przekierowanie zostają w jednym komponencie | Plan |
| Proweniencja | Kolumna `kind`; `original_file_name` zostaje `NOT NULL` | Mówi, czym wiersz jest, zamiast wnioskować to z pustego stringa; wzorzec `NoteEvent.Kind`. Nullowalność wycofana w przeglądzie implementacji (F1) — niosłaby ten sam fakt drugi raz i psuła rollback | Plan + review |
| Limit wklejki | 128 K **znaków** (import: 128 KB **bajtów**) | Kto może zaimportować dokument jako plik, ma móc wkleić jego treść — asymetria czytałaby się jak błąd | Plan |
| Granica SignalR | `max(notatka, wklejka) × 3 + framing` = 458 752 B | Wklejka staje się największą rzeczą przechodzącą przez hub; przekroczenie zrywa obwód, a nie zwraca błąd. Mnożnik to 3, nie 6: hub negocjuje `blazorpack` (surowy UTF-8), nie JSON — skorygowane w przeglądzie implementacji (F3) | Plan (z `lessons.md`) |
| Walidator | Osobny `PasteValidator` + wspólny `Truncate` | Reguły importu dotyczą bajtów i nazwy pliku; wciśnięcie obu w jedną sygnaturę zrobiłoby z każdej gałęzi warunek | Plan |
| Tytuł | Wymagany, podpowiadany z pierwszej linii | Ten sam kontrakt „my proponujemy, ty poprawiasz" co `DeriveTitle` przy imporcie | Plan |
| Przypadki brzegowe | Tylko pusty/białe znaki → odrzucenie; brak ścinania BOM i normalizacji CRLF | BOM to artefakt pliku, nie schowka; rozjechanie wejść w drugą stronę byłoby gorsze niż brak obu | Plan |
| Nadmiar ponad limit | Twarde odrzucenie po stronie serwera | `maxlength` w przeglądarce to wygoda, nie zabezpieczenie — bez gałęzi serwerowej nadmiar zrywa obwód | Plan |
| Zakres testów | `PasteValidator` + arytmetyka granicy transportu | To dwie rzeczy, które w tym plasterku psują się po cichu; ścieżka bazodanowa jest już pokryta | Plan |

## Scope

**In scope:** kolumna `kind` i migracja z backfillem; `PasteValidator` + `PasteResult`; wspólny `TextLimits.Truncate`; przeliczona granica huba; zakładka wklejania na `/materials/import`; rozgałęziona kopia na stronie szczegółów; testy walidatora i granicy.

**Out of scope:** lista notatek i materiałów (S-03); edycja materiału (S-04, zablokowane OQ1); usuwanie (S-05/S-06); zmiany w prompcie, modelu lub `NotePrompt.Version`; normalizacja CRLF i ścinanie BOM; zmiana limitu importu lub notatki; nowa trasa (adres `/materials/import` zostaje).

## Architecture / Approach

```
[zakładka Wklej tekst] ──> PasteValidator.Validate(string) ─┐
                                                            ├─> SourceMaterial { Kind, Content } ──> UserScopedDbContextFactory ──> /materials/{id} ──> istniejąca pętla generowania
[zakładka Plik .md]    ──> MarkdownImportValidator.Validate ┘
```

Rozgałęzienie kończy się na wierszu. Wszystko poniżej — prompt, streaming, edytor, zapis, licznik akceptacji — pozostaje nietknięte. Jedyna zmiana poza tą ścieżką to granica transportu SignalR, która musi unieść nowe, największe wejście.

## Phases at a Glance

| Faza | Co dostarcza | Główne ryzyko |
| --- | --- | --- |
| 1. Proweniencja materiału bez pliku | `kind` + migracja z backfillem, rozgałęziona kopia | Migracja dotyka tabeli z danymi użytkowników; backfill `kind` musi poprzedzić `SET NOT NULL` |
| 2. Reguły wklejania i granica transportu | `PasteValidator`, wspólny `Truncate`, przeliczony `MaximumReceiveMessageSize`, testy | Przeoczenie granicy huba daje zerwany obwód i utratę wklejki — awaria, której komponent nie przechwyci |
| 3. Przełącznik wejścia | Zakładki na `/materials/import`, podpowiedź tytułu, polska kopia | Regresja na działającej ścieżce importu; podpowiedź tytułu nadpisująca to, co użytkownik wpisał |

**Prerequisites:** S-01c zamknięte (jest — zarchiwizowane 2026-08-17). Dostęp do bazy, żeby zastosować i zweryfikować migrację.
**Estimated effort:** ~2-3 sesje, po jednej na fazę.

## Open Risks & Assumptions

- Granica huba rośnie z 448 KB do 832 KB — to sufit na ramkę, nie stała alokacja, ale realnie zwiększa pamięć, jaką jedna ramka może zająć na obwód. Akceptowalne za `[Authorize]` przy `qps: low` z PRD.
- Wklejka 128 K znaków ASCII niesie do prompta ~2× więcej tokenów niż plik na swoim 128 KB limicie bajtów. `NoteGenerator` klasyfikuje odpowiedź „za długi kontekst" jako `TooLong` i pokazuje polski komunikat, więc najgorszy przypadek jest obsłużony — ale to komunikat o błędzie, nie sukces.
- Ta sama liczba (`128 * 1024`) w dwóch różnych jednostkach jest zaproszeniem do „naprawienia" jej na zgodność. Obrona to XML-doc przy obu stałych; nic tego nie wymusza automatycznie.
- Poprawność `kind` nie ma testu automatycznego (świadoma decyzja o zakresie) — wyłapie ją dopiero weryfikacja ręczna fazy 1.

## Success Criteria (Summary)

- Użytkownik wkleja tekst i generuje z niego notatkę tą samą pętlą co z pliku, kończąc na zapisanej notatce pod `/notes/{id}`
- Wklejka ponad limit daje polski komunikat, a nie modal ponownego łączenia i utracony materiał
- Ścieżka importu pliku i materiały zapisane przed tą zmianą działają dokładnie jak wcześniej
