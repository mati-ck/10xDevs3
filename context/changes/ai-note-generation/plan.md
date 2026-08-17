# Generowanie notatki AI obok materiału (S-01b) — Implementation Plan

## Overview

Jednym kliknięciem użytkownik generuje notatkę z zapisanego materiału źródłowego i widzi ją obok źródła, ze strumieniowaną, ciągłą informacją zwrotną w trakcie. Notatka jest ulotna — nie trafia jeszcze na konto (to domyka S-01c), a materiał źródłowy pozostaje nietknięty.

To pierwsza integracja projektu z dostawcą AI. Roadmapa zostawiła wybór dostawcy otwarty (`roadmap.md:125`) — ten plan go domyka: `Microsoft.Extensions.AI` (`IChatClient`) na endpoint OpenRouter zgodny z OpenAI.

## Current State Analysis

Co istnieje po S-01a (`markdown-import`, zarchiwizowane 2026-08-17):

- `Data/Entities/SourceMaterial.cs` — jedyna encja domenowa; `Title` / `Content` / `OriginalFileName`, skalowana właścicielem przez `IOwnedByUser`.
- `Components/Pages/Materials/Detail.razor` — **static SSR, bez circuit**, świadomie: „this page only reads, so it needs no circuit" (linia 10). Renderuje treść w `<pre>`, nigdy jako Markdown/HTML.
- `Components/Pages/Materials/Import.razor` — jedyna strona interaktywna poza auth; wzorzec `isImporting` + `try/finally` + typowane komunikaty po polsku.
- `SourceMaterials/MarkdownImportValidator.cs:26` — limit importu **128 KB**, ustawiony wprost pod ten plasterek: „keeps any imported document comfortably inside an LLM context window, so the generation slice never has to chunk or truncate".
- `Auth/SupabaseAuthClient.cs` — wzorzec integracji z usługą zewnętrzną: typowany `HttpClient`, klasa opcji z konfiguracji, awarie sieci/timeouty mapowane na enum, **żadna proza dostawcy nie dociera do UI**.
- `tests/10xNotes.Tests/DataAccessBoundaryTests.cs` — test blokuje build (i deploy), jeśli jakikolwiek typ aplikacji przyjmie `DbContext` **lub** `IDbContextFactory<>`.

Czego brakuje: pakietu AI, konfiguracji klucza, jakiegokolwiek wywołania modelu, licznika zużycia i interaktywności na stronie materiału.

## Desired End State

Zalogowany użytkownik otwiera `/materials/{id}` i widzi dwie kolumny: materiał źródłowy po lewej, pusty panel notatki po prawej z przyciskiem **„Generuj notatkę"**. Po kliknięciu notatka pojawia się słowo po słowie w prawej kolumnie; przycisk jest zablokowany do końca. Po zakończeniu notatka stoi obok źródła — bez zapisu, bez ustawień. Odświeżenie strony traci notatkę (to zgodne z zakresem: zapis należy do S-01c).

Weryfikacja: `dotnet test` przechodzi, `dotnet build` przechodzi, a ręcznie — wygenerowanie notatki z zaimportowanego materiału i zobaczenie tekstu, który przyrasta na ekranie w trakcie generowania.

### Key Discoveries:

- **`Detail.razor` musi zmienić tryb renderowania.** Dziś jest static SSR bez circuit (`Detail.razor:10`). Streaming wymaga `@rendermode InteractiveServer`. Komentarz w pliku trzeba zaktualizować, bo przestaje być prawdziwy.
- **Limit 128 KB już rozstrzyga chunking.** ~35k tokenów w najgorszym wypadku — mieści się w każdym rozważanym modelu, więc plan nie zawiera dzielenia ani skracania materiału.
- **OpenRouter działa przez `IChatClient` bez własnego konektora** — potwierdzone przez zespół .NET w [dotnet/extensions#6689](https://github.com/dotnet/extensions/issues/6689): `new OpenAI.Chat.ChatClient(model, credential, new() { Endpoint = new Uri("https://openrouter.ai/api/v1") }).AsIChatClient()`.
- **Wersje pakietów:** `Microsoft.Extensions.AI` 10.9.0 i `Microsoft.Extensions.AI.OpenAI` 10.9.0 (ciągnie `OpenAI` 2.13.0) — ta sama linia 10.x co pakiety EF w `10xnotes.csproj`.
- **Koszt:** `google/gemini-3.7-flash` — $0.375/M wejście, $1.875/M wyjście, kontekst 1M (sprawdzone w katalogu OpenRouter 2026-08-17). Materiał 128 KB + notatka ~1.5k tokenów ≈ **$0.016 za generowanie**; limit 50/dobę to maksymalnie ~$0.80 dziennie.
- **RLS jest obowiązkowe dla nowej tabeli** — `lessons.md` („Enable RLS in the same migration that creates the table") oraz wzorzec z `Migrations/20260817162727_AddSourceMaterial.cs:48`: FK do `auth.users` z `ON DELETE CASCADE` + `ENABLE ROW LEVEL SECURITY` bez polityk.
- **CI nie ma sieci ani sekretów** (`deploy.yml:33`) — więc generator testujemy przez podstawiony `IChatClient`, a nie przez HTTP.
- **`DataAccessBoundaryTests` obejmie nowy serwis limitu** — musi brać `UserScopedDbContextFactory`, nigdy `IDbContextFactory<>`.

## What We're NOT Doing

- **Zapisu notatki na koncie** — encja notatki, edytor i „zapis = akceptacja" to S-01c (`note-review-save`). Ten plasterek kończy się notatką na ekranie.
- **Wklejania tekstu jako źródła** — to S-02 (`paste-text-generation`).
- **Parametrów generowania** — PRD Business Logic wymaga jednego kliknięcia bez ustawień. Żadnego wyboru modelu, długości ani stylu w UI.
- **Regeneracji z zachowaniem historii** — ponowne kliknięcie po prostu nadpisuje panel.
- **Renderowania notatki jako Markdown/HTML** — treść modelu trafia na ekran jako tekst, tak jak materiał źródłowy dziś. Sanityzacja i podgląd Markdown to sprawa edytora w S-01c.
- **Retry z backoffem** — nieudane generowanie użytkownik ponawia kliknięciem. Automatyczny retry na limitowanym API pali kwotę.
- **Obserwowalności ponad logi** — `UseOpenTelemetry()` i metryki nie są wymuszane przez żadne NFR.

## Implementation Approach

Trzy fazy, każda samodzielnie weryfikowalna:

1. **Szew generowania** — cała logika AI za jednym interfejsem, testowana bez sieci. Nic w UI.
2. **Rejestr limitu dobowego** — nowa encja + migracja + serwis rezerwujący slot przed wywołaniem.
3. **Strona dwukolumnowa ze strumieniem** — dopiero tu zmienia się tryb renderowania i pojawia się przycisk.

Kolejność jest wymuszona: limit musi istnieć **zanim** przycisk trafi na stronę, inaczej pierwsza wersja UI potrafi wypalić kwotę. Faza 3 spina oba szwy.

Podział odpowiedzialności kopiuje `MarkdownImportValidator` → `Import.razor`: serwis zwraca typowany wynik, strona mapuje go na polską kopię. Dzięki temu gałęzie najbardziej podatne na błąd testują się bez przeglądarki i bez bazy.

## Critical Implementation Details

**Timing & lifecycle.** Streaming przez circuit Blazor Server ma trzy pułapki, których nie widać z listy plików:

1. Każdy `StateHasChanged()` na chunk zalewa SignalR setkami mikro-diffów. Repaint musi być dławiony (bufor + odświeżanie co ~100 ms), a nie wywoływany na każdy token.
2. Wyjście użytkownika ze strony w trakcie generowania nie przerywa samo z siebie `await foreach` — bez `IAsyncDisposable` na komponencie i `CancellationTokenSource` powiązanego z jego życiem strumień leci dalej, płacąc za tokeny, których nikt nie zobaczy.
3. `OperationCanceledException` z anulowania własnym tokenem i ten sam wyjątek z timeoutu to dwie różne historie dla użytkownika. Rozróżnia je stan tokenu (`linkedCts.Token.IsCancellationRequested` vs `timeoutCts.IsCancellationRequested`), nie typ wyjątku.

**State sequencing.** Slot limitu rezerwujemy **przed** wywołaniem modelu, nie po. Odwrotna kolejność sprawia, że seria nieudanych, ale kosztownych wywołań (timeout po 90 s na już policzonych tokenach) nie obciąża licznika w ogóle. Konsekwencja jest świadoma: anulowane generowanie liczy się do limitu.

## Phase 1: Szew generowania i wiring dostawcy

### Overview

Cała rozmowa z modelem za jednym typem, z typowanymi porażkami i wersjonowanym promptem. Bez UI, bez bazy, bez sieci w testach.

### Changes Required:

#### 1. Pakiety

**File**: `10xnotes.csproj`

**Intent**: Wprowadzić abstrakcję `IChatClient` i implementację po stronie OpenAI, żeby zmiana modelu lub dostawcy była zmianą konfiguracji, a nie kodu.

**Contract**: `<PackageReference Include="Microsoft.Extensions.AI" Version="10.9.0" />` i `<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.9.0" />` w istniejącym `ItemGroup`. Ta druga ciągnie `OpenAI` 2.13.0 tranzytywnie — nie referencjonować go osobno.

#### 2. Opcje konfiguracji

**File**: `Generation/AiOptions.cs` (nowy)

**Intent**: Trzymać endpoint, model, klucz i limity w jednym miejscu wiązanym z konfiguracją — dokładnie jak `SupabaseAuthOptions` dla auth.

**Contract**: `sealed class AiOptions` z `SectionName = "Ai"`; właściwości `Endpoint` (domyślnie `https://openrouter.ai/api/v1`), `Model` (domyślnie `google/gemini-3.7-flash`), `ApiKey` (bez domyślnej — sekret), `RequestTimeout` (`TimeSpan`, domyślnie 2 min), `DailyGenerationLimit` (`int`, domyślnie 50).

Klucz **nie** trafia do `appsettings.json` — inaczej niż `Supabase:AnonKey`, który jest publikowalny z definicji. Wzorem `ConnectionStrings__Postgres`: `dotnet user-secrets` lokalnie, zmienna środowiskowa `Ai__ApiKey` na Coolify.

#### 3. Prompt

**File**: `Generation/NotePrompt.cs` (nowy)

**Intent**: Jeden polski prompt systemowy jako wersjonowana stała, żeby każda iteracja jakości była recenzowalnym diffem — miara 75% akceptacji ma sens tylko wobec stabilnego promptu.

**Contract**: `static class NotePrompt` z `public const string Version` (np. `"v1"`) i `public const string System`. Prompt żąda: zwięzłego streszczenia, po nim uporządkowanego konspektu punktów/nagłówków (PRD Business Logic), oraz **odpowiedzi w języku materiału źródłowego** — nie zawsze po polsku, bo użytkownik importuje też materiały angielskie. Metoda budująca wiadomość użytkownika przyjmuje tytuł i treść materiału.

`Version` istnieje po to, żeby S-01c mógł kiedyś zapisać, którym promptem powstała notatka; tutaj jest tylko stałą.

#### 4. Wynik generowania

**File**: `Generation/GenerationFailure.cs` (nowy)

**Intent**: Nazwać każdy sposób, w jaki generowanie może się nie udać, żeby strona mapowała enum na kopię zamiast zgadywać z wyjątku.

**Contract**: `enum GenerationFailure { Unavailable, RateLimited, QuotaExceeded, TooLong, TimedOut, Cancelled, Empty }`. `QuotaExceeded` dotyczy naszego licznika (Faza 2), `RateLimited` — odpowiedzi 429 od dostawcy; rozdzielone, bo jedno użytkownik przeczeka do jutra, a drugie do następnej minuty.

#### 5. Generator

**File**: `Generation/NoteGenerator.cs` (nowy)

**Intent**: Zamienić materiał źródłowy w strumień fragmentów notatki, tłumacząc wszystkie wyjątki dostawcy na `GenerationFailure`. Jedyny typ w aplikacji, który wie o istnieniu `IChatClient`.

**Contract**: Konstruktor przyjmuje `IChatClient`, `IOptions<AiOptions>`, `ILogger<NoteGenerator>`. Metoda zwraca `IAsyncEnumerable<GenerationChunk>`, gdzie `GenerationChunk` niesie albo fragment tekstu, albo terminalne `GenerationFailure`.

Porażka jako element strumienia, nie jako wyjątek — inaczej wołający musi opakowywać `await foreach` w `try/catch`, a `yield return` i `catch` nie współistnieją w tym samym bloku w C#. To ten rzadki przypadek, w którym kontrakt jest nieoczywisty i wymaga podpisu w planie:

```csharp
public readonly record struct GenerationChunk(string? Text, GenerationFailure? Failure)
{
    public static GenerationChunk Content(string text) => new(text, null);
    public static GenerationChunk Failed(GenerationFailure failure) => new(null, failure);
}

IAsyncEnumerable<GenerationChunk> GenerateAsync(
    SourceMaterial material,
    CancellationToken cancellationToken);
```

Klasyfikacja: `ClientResultException` ze statusem 429 → `RateLimited`; 401/403 → `Unavailable` (zalogowane jako błąd konfiguracji, nie jako wina użytkownika); 413 lub błąd kontekstu → `TooLong`; `HttpRequestException` → `Unavailable`; `OperationCanceledException` → `TimedOut` albo `Cancelled` zależnie od tego, który token zadziałał; pusty strumień → `Empty`. Żadna proza dostawcy nie wychodzi na zewnątrz — logowana, nie zwracana (posture z `SupabaseAuthClient`).

#### 6. Rejestracja w DI

**File**: `Program.cs`

**Intent**: Zarejestrować `IChatClient` wskazujący na OpenRouter oraz `NoteGenerator`, obok istniejącej rejestracji `SupabaseAuthClient`.

**Contract**: `builder.Services.Configure<AiOptions>(...)` dla sekcji `Ai`; `AddChatClient(...)` budujący `OpenAI.Chat.ChatClient` z `ApiKeyCredential` i `OpenAIClientOptions { Endpoint = ... }`, zakończony `.AsIChatClient()`; `NoteGenerator` jako scoped. `IChatClient` jest domyślnie singletonem — to poprawne, klient jest bezstanowy i dzieli pulę HTTP.

#### 7. Testy generatora

**File**: `tests/10xNotes.Tests/NoteGeneratorTests.cs` (nowy)

**Intent**: Przypiąć klasyfikację porażek i kształt promptu bez sieci i bez klucza — CI ich nie ma z założenia (`deploy.yml:33`).

**Contract**: Prywatny `FakeChatClient : IChatClient` zwracający zaplanowaną sekwencję `ChatResponseUpdate` albo rzucający zadany wyjątek. Testy: fragmenty docierają w kolejności; 429 → `RateLimited`; 401 → `Unavailable`; awaria sieci → `Unavailable`; timeout → `TimedOut`; anulowanie wołającego → `Cancelled`; pusta odpowiedź → `Empty`; treść materiału trafia do wiadomości użytkownika, a prompt systemowy jest wysyłany; żadna proza wyjątku dostawcy nie pojawia się w `GenerationChunk`.

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `NoteGeneratorTests` pokrywa każdą wartość `GenerationFailure` osiągalną bez bazy
- `DataAccessBoundaryTests` nadal przechodzi (nowe typy nie biorą `DbContext` ani `IDbContextFactory<>`)

#### Manual Verification:

- `dotnet run` startuje bez ustawionego `Ai__ApiKey` — brak klucza nie może wywrócić procesu przy starcie, tylko zawieść wywołanie

---

## Phase 2: Rejestr limitu dobowego

### Overview

Trwały licznik generowań na użytkownika i dobę, chroniący kwotę u dostawcy. Nowa encja, migracja z RLS, serwis rezerwujący slot.

### Changes Required:

#### 1. Encja

**File**: `Data/Entities/GenerationQuota.cs` (nowy)

**Intent**: Zapisać, ile razy dany użytkownik uruchomił generowanie danego dnia, tak żeby licznik przeżył restart i deploy.

**Contract**: `sealed class GenerationQuota : IOwnedByUser` — `Guid Id`, `Guid OwnerId`, `DateOnly UsageDate`, `int Count`, `DateTimeOffset CreatedAt`. Jeden wiersz na parę (właściciel, dzień).

Doba liczona w strefie wyświetlania (`AppTimeProvider`, konfigurowana przez `Display:TimeZone`), nie w UTC — inaczej limit resetuje się użytkownikowi w środku dnia i wygląda na zepsuty.

#### 2. Rejestracja w kontekście

**File**: `Data/AppDbContext.cs`

**Intent**: Dodać `DbSet` i konfigurację encji obok `SourceMaterial`; filtr właściciela dołoży się sam przez `IOwnedByUser`.

**Contract**: `DbSet<GenerationQuota> GenerationQuotas => Set<GenerationQuota>();` oraz blok `modelBuilder.Entity<GenerationQuota>` z `HasKey`, `gen_random_uuid()` na `Id`, `now()` na `CreatedAt` i **unikalnym** indeksem złożonym `(OwnerId, UsageDate)` — inaczej równoległe żądania tworzą dwa wiersze na tę samą dobę i limit przestaje obowiązywać.

#### 3. Migracja

**File**: `Migrations/<timestamp>_AddGenerationQuota.cs` (generowany)

**Intent**: Utworzyć tabelę z tymi samymi dwiema rzeczami, których EF nie wyraża, co przy `source_materials`.

**Contract**: `dotnet ef migrations add AddGenerationQuota`, następnie ręcznie dopisać blok `migrationBuilder.Sql` z FK `owner_id → auth.users(id) ON DELETE CASCADE` oraz `ALTER TABLE public.generation_quotas ENABLE ROW LEVEL SECURITY;` (bez polityk). `Down` zdejmuje FK przed `DropTable`. Wzorzec skopiować dosłownie z `Migrations/20260817162727_AddSourceMaterial.cs:48`.

#### 4. Serwis limitu

**File**: `Generation/GenerationQuotaService.cs` (nowy)

**Intent**: Rezerwować slot przed wywołaniem modelu i odmawiać, gdy dobowy limit jest wyczerpany.

**Contract**: Konstruktor bierze `UserScopedDbContextFactory`, `TimeProvider`, `IOptions<AiOptions>` — **nigdy** `IDbContextFactory<>` (`DataAccessBoundaryTests` blokuje deploy). Metoda `TryReserveAsync(CancellationToken)` zwraca `bool`: odczytuje lub tworzy wiersz na dzisiejszą datę, odmawia gdy `Count >= DailyGenerationLimit`, w przeciwnym razie inkrementuje i zapisuje.

`OwnerId` nie jest ustawiane ręcznie — `AppDbContext.StampOwners` stempluje je z zalogowanego użytkownika i nadpisuje cokolwiek podano.

Wyścig na unikalnym indeksie (dwie karty, pierwszy zapis dnia) łapiemy jako `DbUpdateException` i ponawiamy raz odczyt-inkrementację; drugie niepowodzenie zwraca `false`. Bez tego pierwsze generowanie w dwóch kartach naraz wywala stronę zamiast policzyć jedno.

#### 5. Rejestracja i testy

**File**: `Program.cs`, `tests/10xNotes.Tests/GenerationQuotaTests.cs` (nowy)

**Intent**: Zarejestrować serwis jako scoped i udowodnić, że licznik jest skalowany właścicielem oraz że limit faktycznie odmawia.

**Contract**: Testy na SQLite in-memory, wzorem `OwnerScopingTests`: licznik jednego użytkownika nie jest widoczny dla drugiego; `TryReserveAsync` zwraca `true` do limitu włącznie i `false` powyżej; nowy dzień zaczyna liczenie od zera (sterowane podstawionym `TimeProvider`); zapis bez zalogowanego użytkownika rzuca.

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `GenerationQuotaTests` dowodzi izolacji per użytkownik, odmowy po przekroczeniu limitu i resetu następnego dnia
- `DataAccessBoundaryTests` przechodzi — `GenerationQuotaService` bierze `UserScopedDbContextFactory`
- Migracja stosuje się czysto: `dotnet ef database update`

#### Manual Verification:

- Supabase security advisor nie zgłasza `generation_quotas` jako tabeli bez RLS (obowiązkowe wg `lessons.md`)
- Po starcie aplikacji tabela istnieje, a `/health/ready` zwraca `Healthy`

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej zatrzymaj się i poczekaj na potwierdzenie ręcznego testu przed Fazą 3.

---

## Phase 3: Strona dwukolumnowa ze strumieniowaniem

### Overview

`/materials/{id}` staje się interaktywna: materiał po lewej, notatka po prawej, przycisk generowania i tekst przyrastający na ekranie.

### Changes Required:

#### 1. Tryb renderowania i układ

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Przełączyć stronę na `InteractiveServer` i rozłożyć ją na dwie kolumny — źródło obok notatki, co jest wprost guardrailem PRD („materiał zawsze pozostaje dostępny obok notatki").

**Contract**: `@rendermode InteractiveServer` na górze pliku. Siatka Bootstrap 5.3 (`row` / `col-lg-6`) — na wąskim oknie kolumny składają się pionowo, źródło pierwsze. Komentarz z linii 10–12 („this page only reads, so it needs no circuit") przestaje być prawdziwy i musi zostać zastąpiony wyjaśnieniem, dlaczego strona teraz bierze circuit.

Nagłówek, data i `<pre>` z treścią zostają bez zmian w lewej kolumnie — materiał źródłowy dalej nigdy nie trafia do DOM jako HTML.

#### 2. Panel notatki i strumieniowanie

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Uruchomić generowanie jednym kliknięciem i pokazywać notatkę w miarę jej powstawania.

**Contract**: Prawa kolumna: przycisk „Generuj notatkę" (zablokowany w trakcie, etykieta „Generuję…"), `<pre>` z buforem notatki, `<div class="alert alert-danger">` z komunikatem porażki. Handler najpierw woła `GenerationQuotaService.TryReserveAsync`, potem konsumuje `NoteGenerator.GenerateAsync`.

Repaint dławiony: fragmenty dopisywane do `StringBuilder`, `StateHasChanged()` wołane nie częściej niż co ~100 ms plus raz na koniec. Bez tego każdy token to osobny diff przez SignalR.

Komponent implementuje `IAsyncDisposable` i trzyma `CancellationTokenSource` powiązany z generowaniem, anulowany przy `DisposeAsync` — inaczej wyjście ze strony zostawia strumień lecący i płacący za tokeny.

Porażka **czyści panel** i pokazuje komunikat (decyzja: bez zachowywania częściowego tekstu), więc to, co na ekranie, jest zawsze notatką kompletną — nigdy uciętą.

#### 3. Polska kopia porażek

**File**: `Components/Pages/Materials/Detail.razor`

**Intent**: Zamienić `GenerationFailure` na komunikat, na który użytkownik może zareagować — wzorem `MessageFor` z `Import.razor:170`.

**Contract**: `switch` po `GenerationFailure` → polski tekst. `QuotaExceeded` mówi o dziennym limicie i o tym, że odnowi się jutro; `RateLimited` prosi o chwilę przerwy; `TimedOut` proponuje ponowienie; `Unavailable` jest neutralny i nie obwinia użytkownika; `TooLong` mówi o zbyt długim materiale. Domyślna gałąź istnieje, żeby dodanie wartości do enuma nie zostawiło pustego alertu.

#### 4. Wejście od importu

**File**: `Components/Pages/Materials/Import.razor` (weryfikacja, prawdopodobnie bez zmian)

**Intent**: Upewnić się, że po imporcie użytkownik ląduje na stronie, na której przycisk generowania już jest — nawigacja z linii 132 prowadzi do `/materials/{id}`, więc przepływ domyka się sam.

**Contract**: Bez zmian w kodzie, jeśli nawigacja działa jak dziś. Zweryfikować ręcznie w kroku 1 testów manualnych.

### Success Criteria:

#### Automated Verification:

- Kompilacja przechodzi: `dotnet build`
- Cały pakiet testów przechodzi: `dotnet test`
- `DataAccessBoundaryTests` przechodzi po zmianie trybu renderowania strony

#### Manual Verification:

- Import pliku `.md` prowadzi na stronę materiału, na której widać przycisk „Generuj notatkę"
- Kliknięcie uruchamia generowanie i tekst **przyrasta na ekranie** w trakcie, a nie pojawia się dopiero na końcu
- Notatka jest streszczeniem plus uporządkowanym konspektem, w języku materiału źródłowego
- Materiał źródłowy jest nietknięty po generowaniu (guardrail PRD)
- Odświeżenie strony traci notatkę — nic nie zostało zapisane na koncie
- Wyjście ze strony w trakcie generowania nie zostawia błędu w logach ani nie wywraca circuit
- Wyczerpanie limitu dobowego pokazuje komunikat o limicie, nie ogólny błąd
- Brak `Ai__ApiKey` daje neutralny komunikat, nie ekran błędu

**Implementation Note**: Po tej fazie i przejściu weryfikacji automatycznej zatrzymaj się i poczekaj na potwierdzenie ręcznego testu.

---

## Testing Strategy

### Unit Tests:

- `NoteGeneratorTests` — klasyfikacja każdej porażki, kolejność fragmentów, obecność promptu systemowego i treści materiału w żądaniu, brak prozy dostawcy w wyniku
- `GenerationQuotaTests` — izolacja per właściciel, odmowa po przekroczeniu limitu, reset o północy w strefie wyświetlania, rzucenie przy braku zalogowanego użytkownika
- `DataAccessBoundaryTests` (istniejący) — obejmie nowe typy automatycznie

### Integration Tests:

Brak nowych. Cała warstwa danych jest testowana na SQLite in-memory tak jak dotąd, a warstwa AI przez podstawiony `IChatClient` — `deploy.yml` uruchamia pakiet bez sieci i bez sekretów i to musi zostać.

### Manual Testing Steps:

1. Ustaw `Ai:ApiKey` przez `dotnet user-secrets`, uruchom `dotnet run`, zaloguj się.
2. Zaimportuj plik `.md` (kilka stron tekstu) — powinieneś wylądować na stronie materiału z widocznym przyciskiem generowania.
3. Kliknij „Generuj notatkę" — obserwuj, czy tekst przyrasta, a przycisk jest zablokowany.
4. Po zakończeniu porównaj notatkę ze źródłem obok: streszczenie plus punkty, w języku materiału.
5. Odśwież stronę — notatka znika, materiał zostaje.
6. Wygeneruj ponownie i w trakcie przejdź na inną stronę; sprawdź logi pod kątem niedomkniętego strumienia.
7. Zaimportuj materiał po angielsku i sprawdź, że notatka jest po angielsku.
8. Tymczasowo ustaw `Ai:DailyGenerationLimit` na 1, wygeneruj dwa razy — drugie kliknięcie musi dać komunikat o limicie dobowym.
9. Tymczasowo zepsuj `Ai:ApiKey` — komunikat neutralny, żadnej prozy dostawcy, żadnego ekranu błędu.

## Performance Considerations

Jedyne realne obciążenie to strumień przez circuit: bez dławienia repaintów wolny model generuje setki mikro-aktualizacji SignalR na notatkę. Bufor plus odświeżanie co ~100 ms sprowadza to do kilkudziesięciu.

Materiał w pamięci to najwyżej 128 KB — nieistotne. Zapytanie o limit to jeden wiersz po indeksie unikalnym `(owner_id, usage_date)`.

Timeout żądania (domyślnie 2 min) jest jedyną granicą czasu; strona nie ma własnego.

## Migration Notes

Jedna nowa tabela, `generation_quotas`. Migracja jest addytywna i wstecznie zgodna — rollback Coolify nie cofa migracji (`CLAUDE.md`), a starsza wersja aplikacji po prostu nie zna tabeli i działa dalej.

Nic nie trzeba backfillować: brak wiersza znaczy „zero generowań dzisiaj".

Nowa zmienna środowiskowa na Coolify: **`Ai__ApiKey`** (podwójne podkreślenie). Opcjonalnie `Ai__Model`, `Ai__DailyGenerationLimit`. Bez klucza aplikacja startuje normalnie, a generowanie zwraca `Unavailable` — deploy nie może się wywrócić z powodu braku sekretu.

## References

- Roadmapa: `context/foundation/roadmap.md` (S-01b, linie 116–127) — w tym otwarte pytanie o dostawcę, domknięte tutaj
- PRD: `context/foundation/prd.md` — US-01, FR-005, Business Logic, NFR o widocznej informacji zwrotnej >2s
- Lekcja o RLS: `context/foundation/lessons.md`
- Poprzedni plasterek: `context/archive/2026-08-17-markdown-import/plan.md`
- Wzorzec integracji zewnętrznej: `Auth/SupabaseAuthClient.cs`, `tests/10xNotes.Tests/SupabaseAuthClientTests.cs`
- Wzorzec migracji z RLS: `Migrations/20260817162727_AddSourceMaterial.cs:48`
- OpenRouter przez `IChatClient`: [dotnet/extensions#6689](https://github.com/dotnet/extensions/issues/6689)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Szew generowania i wiring dostawcy

#### Automated

- [x] 1.1 Kompilacja przechodzi: `dotnet build` — 6a8707c
- [x] 1.2 Cały pakiet testów przechodzi: `dotnet test` — 6a8707c
- [x] 1.3 `NoteGeneratorTests` pokrywa każdą wartość `GenerationFailure` osiągalną bez bazy — 6a8707c
- [x] 1.4 `DataAccessBoundaryTests` nadal przechodzi — 6a8707c

#### Manual

- [x] 1.5 `dotnet run` startuje bez ustawionego `Ai__ApiKey` — 6a8707c

### Phase 2: Rejestr limitu dobowego

#### Automated

- [x] 2.1 Kompilacja przechodzi: `dotnet build` — 4d0e7d2
- [x] 2.2 Cały pakiet testów przechodzi: `dotnet test` — 4d0e7d2
- [x] 2.3 `GenerationQuotaTests` dowodzi izolacji, odmowy i resetu dobowego — 4d0e7d2
- [x] 2.4 `DataAccessBoundaryTests` przechodzi — serwis bierze `UserScopedDbContextFactory` — 4d0e7d2
- [x] 2.5 Migracja stosuje się czysto: `dotnet ef database update` — 4d0e7d2

#### Manual

- [x] 2.6 Supabase security advisor nie zgłasza `generation_quotas` bez RLS — 4d0e7d2
- [x] 2.7 Tabela istnieje po starcie, `/health/ready` zwraca `Healthy` — 4d0e7d2

### Phase 3: Strona dwukolumnowa ze strumieniowaniem

#### Automated

- [x] 3.1 Kompilacja przechodzi: `dotnet build` — 6bc4fd3
- [x] 3.2 Cały pakiet testów przechodzi: `dotnet test` — 6bc4fd3
- [x] 3.3 `DataAccessBoundaryTests` przechodzi po zmianie trybu renderowania — 6bc4fd3

#### Manual

- [x] 3.4 Import prowadzi na stronę z przyciskiem „Generuj notatkę" — 6bc4fd3
- [x] 3.5 Tekst przyrasta na ekranie w trakcie generowania — 6bc4fd3
- [x] 3.6 Notatka to streszczenie plus konspekt, w języku materiału — 6bc4fd3
- [x] 3.7 Materiał źródłowy nietknięty po generowaniu — 6bc4fd3
- [x] 3.8 Odświeżenie strony traci notatkę — nic nie zapisano — 6bc4fd3
- [x] 3.9 Wyjście w trakcie generowania nie zostawia błędu ani nie wywraca circuit — 6bc4fd3
- [x] 3.10 Wyczerpany limit dobowy daje komunikat o limicie — 6bc4fd3
- [x] 3.11 Brak `Ai__ApiKey` daje neutralny komunikat, nie ekran błędu — 6bc4fd3
