---
project: "10xNotes"
version: 2
status: draft
created: 2026-05-28
context_type: greenfield
product_type: web-app
target_scale:
  users: medium
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

# 10xNotes — Product Requirements Document

## Vision & Problem Statement

Przeglądanie długich materiałów jest czasochłonne i wiąże się z barierą startu — sam fakt, że trzeba usiąść i przerobić obszerny tekst, zniechęca do powrotu do własnych notatek i materiałów. W efekcie zgromadzona wiedza leży nietknięta.

Insight: najtrudniejszy jest pierwszy krok. Zamiast wymagać od użytkownika napisania notatki od zera, aplikacja generuje wstępny szkic na podstawie przesłanego materiału — człowiek tylko go poprawia i zatwierdza. To przesuwa wysiłek z „napisz" na „popraw", co usuwa barierę startu.

## User & Persona

Pojedynczy użytkownik, który gromadzi własne długie materiały (wykłady, dokumenty, artykuły) i chce z nich szybko korzystać. Buduje to przede wszystkim dla siebie i osób o podobnych potrzebach. Sięga po produkt w momencie, gdy ma materiał, do którego „kiedyś trzeba wrócić", ale długość i brak struktury go odpychają.

## Success Criteria

### Primary
- 75% notatek wygenerowanych przez AI jest akceptowanych przez użytkownika.
- Pełny przepływ działa end-to-end: logowanie → import tekstu/Markdown → wygenerowanie notatki przez AI → przegląd/edycja/akceptacja.

### Secondary
- Powracalność: użytkownik wraca i generuje notatki z więcej niż jednego materiału w kolejnych sesjach.

### Guardrails
- Prywatność: przesłany materiał i notatki jednego użytkownika nigdy nie są widoczne dla innych użytkowników.
- Zachowanie źródła: oryginalny materiał zawsze pozostaje dostępny obok notatki; generowanie notatki nie nadpisuje ani nie usuwa materiału źródłowego.

## User Stories

### US-01: Wygenerowanie notatki z materiału źródłowego

- **Given** zalogowany użytkownik z przesłanym materiałem źródłowym (tekst lub Markdown)
- **When** uruchamia generowanie notatki
- **Then** widzi wygenerowaną notatkę obok materiału źródłowego i może ją zapisać (akceptacja) lub porzucić (odrzucenie)

#### Acceptance Criteria
- Materiał źródłowy pozostaje dostępny i niezmieniony po wygenerowaniu notatki.
- Zapisanie notatki wiąże ją z kontem użytkownika i liczy się jako akceptacja.
- Porzucenie wygenerowanej notatki bez zapisu nie pozostawia jej na koncie.
- Operacja generowania daje widoczną informację zwrotną w trakcie trwania.

## Functional Requirements

### Konto
- FR-001: Użytkownik może założyć konto (email + hasło). Priority: must-have
  > Socrates: Rozważono kontrargument „konto = tarcie / własne hasła to ciężar". Rozstrzygnięcie: zostaje — dane muszą być wiązane z użytkownikiem.
- FR-002: Użytkownik może się zalogować i wylogować. Priority: must-have
  > Socrates: Rozważono „wylogowanie zbędne w MVP". Rozstrzygnięcie: zostaje — podstawa dostępu do własnych danych.
- FR-012: Użytkownik może ustawić i wyczyścić nazwę wyświetlaną swojego konta. Priority: nice-to-have
- FR-013: Zalogowany użytkownik może zmienić swoje hasło, podając hasło dotychczasowe. Priority: must-have
- FR-014: Użytkownik może usunąć swoje konto wraz ze wszystkimi swoimi danymi. Priority: must-have

> **FR-012…FR-014 dopisane 2026-09-08**, po pierwotnej redakcji PRD (v1) i poza rundą Sokratesa, której poddane były FR-001…FR-011 — stąd brak przy nich linii `> Socrates:`. Numeracja jest ciągła względem całego dokumentu, a nie względem sekcji: FR-003…FR-011 mają stabilne identyfikatory, do których odwołuje się `roadmap.md`, więc przenumerowanie ich byłoby gorsze niż skok numeracji w tej sekcji. Źródło: decyzja produktowa przy planowaniu `user-profile` (S-07), nie pierwotne odkrycie potrzeb.

### Import materiału źródłowego
- FR-003: Użytkownik może wkleić tekst jako materiał źródłowy. Priority: must-have
  > Socrates: Rozważono „wystarczy import pliku". Rozstrzygnięcie: zostaje — wklejanie to najniższy próg wejścia do pierwszej notatki.
- FR-004: Użytkownik może zaimportować plik Markdown jako materiał źródłowy. Priority: must-have
  > Socrates: Rozważono „duplikat wklejania". Rozstrzygnięcie: zostaje — import pliku to osobny przypadek użycia (wygoda przy długich materiałach).

### Generowanie notatek
- FR-005: Użytkownik może wygenerować notatkę z materiału źródłowego przy pomocy AI. Priority: must-have
  > Socrates: Rozważono „ryzyko, że jakość AI nie domknie 75%". Rozstrzygnięcie: zostaje — to sedno produktu; ryzyko jakości jest świadome.
- FR-006: Użytkownik może zapisać wygenerowaną notatkę; zapis liczy się jako akceptacja, a porzucenie bez zapisu jako odrzucenie. Priority: must-have
  > Socrates: Rozważono „zapis to słaby sygnał akceptacji". Rozstrzygnięcie: zostaje — najprostszy model na MVP.

### Zarządzanie treścią
- FR-007: Użytkownik może przeglądać swoje notatki i materiały źródłowe. Priority: must-have
  > Socrates: Rozważono „po co osobny widok materiałów". Rozstrzygnięcie: zostaje, ale materiał źródłowy jest dostępny przede wszystkim obok swojej notatki, niekoniecznie jako osobna lista najwyższego poziomu (do rozstrzygnięcia w designie).
- FR-008: Użytkownik może edytować wygenerowaną notatkę. Priority: must-have
  > Socrates: Rozważono „skoro AI dobre, po co edycja". Rozstrzygnięcie: zostaje — edycja to istota insightu „AI robi szkic, człowiek poprawia".
- FR-009: Użytkownik może edytować materiał źródłowy. Priority: must-have
  > Socrates: Rozważono „źródło to zapis historyczny — edycja podważa wierność notatki". Uznano za zasadne. Rozstrzygnięcie: zostaje w zakresie, ale otwarte pytanie o to, czy źródło ma być edytowalne po wygenerowaniu i co z notatką (patrz Open Questions Q1).
- FR-010: Użytkownik może usunąć notatkę. Priority: must-have
  > Socrates: Rozważono „usuwanie zbędne w MVP". Rozstrzygnięcie: zostaje — jawnie w zakresie.
- FR-011: Użytkownik może usunąć materiał źródłowy. Priority: must-have
  > Socrates: Rozważono „co z powiązanymi notatkami". Uznano za zasadne. Rozstrzygnięcie: zostaje, ale trzeba zdecydować zachowanie kaskady przy usuwaniu źródła (patrz Open Questions Q2).

## Non-Functional Requirements

- Treść jednego użytkownika (materiały źródłowe i notatki) nie jest dostępna żadnemu innemu użytkownikowi ani publicznie.
- Każda operacja trwająca dłużej niż dwie sekundy (w szczególności generowanie notatki) zapewnia użytkownikowi ciągłą, widoczną informację zwrotną o postępie.
- Zapisane materiały i notatki pozostają trwałe i dostępne po ponownym zalogowaniu — nie znikają między sesjami.
- Produkt pozostaje używalny na aktualnych wersjach głównych przeglądarek desktopowych (web-first).

## Business Logic

Aplikacja przekształca długi materiał źródłowy w zwięzłą, ustrukturyzowaną notatkę — streszczenie ujęte w uporządkowany konspekt — którą użytkownik może zaakceptować lub poprawić.

Wejściem reguły jest długi materiał tekstowy dostarczony przez użytkownika (wklejony tekst lub zaimportowany plik Markdown). Wyjściem jest krótsza notatka oddająca esencję materiału w formie streszczenia połączonego z uporządkowanymi punktami/nagłówkami. Użytkownik napotyka regułę po jednym kliknięciu „generuj" — notatka powstaje automatycznie, bez ustawiania parametrów, i pojawia się obok materiału źródłowego do akceptacji (zapisu) lub poprawy.

## Access Control

Uwierzytelnianie kontem: rejestracja i logowanie e-mailem + hasłem. Dane (przesłane materiały i wygenerowane notatki) są wiązane z kontem użytkownika.

Płaski model użytkownika — brak ról. Każdy zalogowany użytkownik widzi i zarządza wyłącznie własnymi materiałami i notatkami; nie ma dostępu do danych innych użytkowników. Niezalogowany użytkownik nie ma dostępu do żadnych notatek.

## Non-Goals

- Import innych formatów plików (PDF, .docx, audio, video) — MVP przyjmuje tylko wklejony tekst i pliki Markdown; reszta formatów wymaga osobnego parsowania i czeka na v2.
- Współdzielenie notatek między użytkownikami — produkt jest jednoosobowy; brak udostępniania utrzymuje prosty, płaski model dostępu.
- Integracje z zewnętrznymi platformami/usługami — żadnych konektorów w MVP, by nie wiązać zakresu z cudzymi API.
- Aplikacja mobilna — tylko web na start; natywny klient mobilny poza zakresem MVP.

## Open Questions

1. **Czy materiał źródłowy jest edytowalny po wygenerowaniu notatki, a jeśli tak — co dzieje się z istniejącą notatką (pozostaje, oznaczana jako nieaktualna, regenerowana)?** — Owner: użytkownik. Wynika z rundy Sokratesa nad FR-009.
2. **Co dzieje się z notatkami powiązanymi z materiałem źródłowym przy jego usunięciu (kaskadowe usunięcie vs osierocenie notatki)?** — Owner: użytkownik. Wynika z rundy Sokratesa nad FR-011.
