# Generowanie notatki AI obok materiału (S-01b) — Plan Brief

> Full plan: `context/changes/ai-note-generation/plan.md`

## What & Why

Jednym kliknięciem użytkownik generuje notatkę z zapisanego materiału źródłowego i widzi ją obok źródła, z tekstem przyrastającym na ekranie w trakcie. Notatka jest na tym etapie ulotna — nie trafia jeszcze na konto. To drugi z trzech kawałków gwiazdy przewodniej i miejsce, w którym domyka się najbardziej ryzykowne założenie produktu: czy AI potrafi zrobić szkic, który człowiek zechce zaakceptować.

## Starting Point

Po S-01a (`markdown-import`) istnieje encja `SourceMaterial`, strona importu i read-only strona materiału pod `/materials/{id}` — świadomie w trybie static SSR, bez circuit. Limit importu 128 KB został ustawiony właśnie pod ten plasterek, żeby materiał mieścił się w oknie kontekstu bez dzielenia. Nie ma jeszcze żadnego pakietu AI, konfiguracji klucza ani interaktywności na stronie materiału.

## Desired End State

`/materials/{id}` jest workspace'em dwukolumnowym: materiał po lewej, panel notatki po prawej z przyciskiem „Generuj notatkę". Kliknięcie uruchamia generowanie, notatka pojawia się słowo po słowie, materiał zostaje nietknięty. Odświeżenie strony traci notatkę — zapis to zakres S-01c.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Dostawca AI (otwarte pytanie roadmapy) | `Microsoft.Extensions.AI` → OpenRouter | `IChatClient` czyni model i dostawcę wartością konfiguracji, a testy podstawiają fałszywego klienta zamiast stubować HTTP | Plan |
| Model domyślny | `google/gemini-3.7-flash`, konto płatne | ~$0.016 za generowanie, kontekst 1M, brak limitów darmowego progu przy mierzeniu akceptacji | Plan |
| Informacja zwrotna | Strumieniowanie tokenów do panelu | Najmocniejsza odpowiedź na NFR „ciągła widoczna informacja zwrotna >2s" — użytkownik widzi treść, nie obietnicę | Plan |
| Układ | Dwie kolumny na stronie materiału | Dosłownie realizuje guardrail PRD „materiał zawsze obok notatki"; S-01c wstawi edytor w prawą kolumnę | Plan |
| Obsługa porażek | Typowany enum, panel czyszczony | To, co na ekranie, jest zawsze notatką kompletną — nigdy uciętą, którą można wziąć za gotową | Plan |
| Prompt | Stała wersjonowana w kodzie | Miara 75% akceptacji ma sens tylko wobec stabilnego, recenzowalnego promptu | Plan |
| Ochrona kwoty | Limit dobowy w tabeli + timeout | Licznik w pamięci ginie przy każdym deployu, a auto-deploy zdarza się często | Plan |

## Scope

**In scope:** pakiety `Microsoft.Extensions.AI` + `.OpenAI`, opcje `Ai` z sekretem klucza, wersjonowany prompt, `NoteGenerator` ze strumieniem i typowanymi porażkami, encja `GenerationQuota` z migracją (FK + RLS), serwis limitu, dwukolumnowa interaktywna strona materiału z dławionym repaintem i anulowaniem.

**Out of scope:** zapis notatki i encja notatki (S-01c), wklejanie tekstu (S-02), parametry generowania (PRD: jedno kliknięcie bez ustawień), renderowanie notatki jako Markdown, automatyczny retry, telemetria.

## Architecture / Approach

`Detail.razor` (Interactive Server) → `GenerationQuotaService.TryReserveAsync` (przez `UserScopedDbContextFactory`) → `NoteGenerator.GenerateAsync` → `IChatClient` → OpenRouter. Generator zwraca `IAsyncEnumerable<GenerationChunk>`, gdzie porażka jest elementem strumienia, nie wyjątkiem; strona mapuje enum na polską kopię — dokładnie tak, jak `MarkdownImportValidator` mapuje się na `Import.razor` dzisiaj. Cała wiedza o dostawcy kończy się na `NoteGenerator`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Szew generowania | Wywołanie modelu za jednym interfejsem, prompt, typowane porażki, testy bez sieci | Klasyfikacja wyjątków SDK OpenAI na porażki — łatwo pomylić timeout z anulowaniem |
| 2. Rejestr limitu | Tabela `generation_quotas` z RLS, serwis rezerwujący slot | Wyścig na unikalnym indeksie przy pierwszym generowaniu dnia w dwóch kartach |
| 3. Strona ze strumieniem | Dwie kolumny, przycisk, tekst przyrastający na ekranie | Streaming przez circuit: dławienie repaintów i anulowanie przy wyjściu ze strony |

**Prerequisites:** S-01a wdrożone (jest); konto OpenRouter z kredytami i klucz w `dotnet user-secrets` lokalnie oraz `Ai__ApiKey` na Coolify.
**Estimated effort:** ~3 sesje, po jednej na fazę.

## Open Risks & Assumptions

- Jakość notatek jest niesprawdzona do pierwszego realnego uruchomienia — prompt niemal na pewno przejdzie kilka iteracji, a każda to deploy (świadomy koszt wersjonowania promptu w kodzie).
- Anulowane generowanie liczy się do limitu dobowego, bo slot rezerwujemy przed wywołaniem; przy limicie 50 to nieistotne, przy niskim byłoby uciążliwe.
- Strona materiału traci static SSR także dla użytkowników, którzy przyszli tylko czytać — akceptowalne przy tej skali, do rozważenia gdyby S-03 pokazał inny wzorzec użycia.
- Prompt prosi o odpowiedź w języku materiału; przy materiale mieszanym językowo wynik jest nieprzewidywalny.

## Success Criteria (Summary)

- Użytkownik klika raz i widzi notatkę powstającą na ekranie obok nietkniętego materiału źródłowego.
- Notatka jest streszczeniem połączonym z uporządkowanym konspektem, w języku materiału (PRD Business Logic).
- Każda awaria — dostawca, limit, timeout — daje komunikat po polsku, na który da się zareagować, i nigdy prozy dostawcy ani ekranu błędu.
