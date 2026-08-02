---
change_id: auth-session-hardening
title: Domknięcie sesji i kontraktu właściciela przed S-01
status: implemented
created: 2026-08-02
updated: 2026-08-02
archived_at: null
---

## Notes

GitHub issue: [#22](https://github.com/mati-ck/10xDevs3/issues/22)

Zebrane z read-only review planu F-02 (`context/archive/2026-07-27-email-password-auth/plan.md`,
przegląd wykonany 2026-08-02, nic nie zapisano do archiwum) oraz z pozycji przeniesionych
z review F-01 (`context/changes/persistence-baseline/reviews/impl-review.md`).

Wspólny mianownik: F-01 i F-02 domknęły ścieżkę odczytu i logowania, ale zostawiły trzy luki
w *sesji* i dwie w *kontrakcie właściciela*. S-01 jest pierwszym wycinkiem, który naprawdę
zapisuje encje należące do użytkownika, więc to ostatni moment, żeby je zamknąć tanio.

### Z review F-02 (2026-08-02)

1. **Stan uwierzytelnienia obwodu nigdy nie jest odświeżany.** `Program.cs:70` używa domyślnego
   `ServerAuthenticationStateProvider`, który ustala tożsamość raz, przy starcie obwodu SignalR,
   i już nigdy jej nie sprawdza — w repo nie ma żadnego `RevalidatingServerAuthenticationStateProvider`.
   Skutek: wylogowanie w jednej karcie nie kończy sesji w drugiej, a wygaśnięcie ciasteczka nie
   kończy obwodu, który już działa. Komentarz w `Components/Account/RedirectToLogin.razor:3-5`
   opisuje dokładnie ten scenariusz jako obsłużony — a nic nie potrafi go dziś wykryć.

2. **[WYPADŁO Z ZAKRESU 2026-08-02 — nie chcemy potwierdzania e-maila w MVP, więc niewymuszalność
   potwierdzania nie jest defektem, tylko konsekwencją decyzji produktowej.]**
   **Rejestracja tworzy ciasteczko bez sprawdzenia, czy GoTrue wydał sesję.**
   `Auth/SupabaseAuthClient.cs:97` przyjmuje `id` z korzenia odpowiedzi — a to jest właśnie kształt,
   jaki GoTrue zwraca dla użytkownika **niepotwierdzonego**, bez sesji. `Register.razor:73` woła
   wtedy `AuthCookie.SignInAsync` bezwarunkowo. Potwierdzanie e-maila jest więc strukturalnie
   niewymuszalne po stronie aplikacji: jedyne, co dziś ratuje sytuację, to przełącznik
   `mailer_autoconfirm: true` w panelu Supabase — ustawienie spoza gita. Włączenie potwierdzania
   „dla bezpieczeństwa" byłoby po cichu bezskuteczne.

3. **`AppDbContext` da się wstrzyknąć wprost z DI, bez właściciela.** Rejestracja w `Program.cs:29`
   istnieje dla magazynu kluczy DataProtection i dla `AddDbContextCheck`, ale nic nie broni
   komponentowi S-01 sięgnąć po nią zamiast po `UserScopedDbContextFactory`. Taki kontekst ma
   `CurrentUserId == Guid.Empty`: odczyt zwraca zero wierszy, zapis rzuca `InvalidOperationException`.
   Fail-closed, więc nie wyciek — ale objawia się jako „baza jest pusta", co jest najwolniejszą
   możliwą diagnozą. Zdanie „the only sanctioned way" z `UserScopedDbContextFactory` jest dziś
   dokumentacją, nie egzekwowaną regułą.

### Przeniesione z review F-01

4. **Resztkowa luka F1** — `attach-by-PK` z podrobionym `OwnerId` nadal przechodzi przez strażnika
   dodanego w `StampOwners`, bo EF celuje w wiersz samym kluczem głównym. Domyka to oznaczenie
   `OwnerId` jako `IsConcurrencyToken()`, co wpycha `owner_id` do klauzuli `WHERE` w `UPDATE`/`DELETE`.
   Zmienia snapshot modelu, więc wymaga migracji.

5. **F6 świadomie pominięte** — strażniki zapisu z F1/F2 nie mają żadnego testu, więc refaktor może
   je usunąć bez sygnału. Brakuje przypadków adwersaryjnych: `OwnerId` podany przez wywołującego
   oraz `attach-by-PK` na cudzym wierszu.

### Ryzyko zaakceptowane (planowanie, 2026-08-02): sesji bezstanowej nie da się unieważnić

Sesja jest bezstanowa z wyboru — ciasteczko podpisane kluczami DataProtection, bez żadnego rekordu
sesji po stronie serwera i bez tabel. Konsekwencja: **wylogowanie w jednej karcie nie kończy obwodu
w drugiej**, a sesji nie da się odwołać przed jej wygaśnięciem. To ta sama właściwość, którą ma
każdy stateless JWT — nie błąd implementacji, tylko cena modelu.

Jedyna dostępna dźwignia to długość okna. Ciasteczko zachowuje 14-dniowe okno przesuwane
(`SlidingExpiration = true`, nietknięte), a obwód dostaje osobny **bezwzględny limit 30 dni** od
zalogowania, niesiony w claimie. To dwie różne liczby o dwóch różnych znaczeniach: przesuwane okno
rządzi zwykłymi żądaniami HTTP, limit ogranicza życie obwodu.

### Ryzyko zaakceptowane (Faza 3, 2026-08-02): przekroczenie limitu nie wylogowuje wizualnie

Zmierzone, nie założone. `Components/App.razor:18` renderuje `<Routes />` bez `@rendermode`, więc
router, `AuthorizeRouteView` i `RedirectToLogin` żyją poza obwodem i nie mają jak zareagować na
unieważnionego principala. Weryfikacja z 2026-08-02: przy skróconym do 15 s interwale i wymuszonym
principalu po limicie pętla rewalidacji zwróciła „nieważna" (`pastCap=True`), a strona się nie
zmieniła — nadal `/counter`, nadal e-mail w nawigacji.

Co limit **daje**: principal w obwodzie staje się anonimowy, więc `ICurrentUserAccessor` zwraca
`null` i każde zapytanie objęte filtrem właściciela zwraca zero wierszy. Obwód przestaje serwować
dane — to jest treść znaleziska. Interfejs dogania przy najbliższym żądaniu HTTP.

Czego **nie da się tanio naprawić**: interaktywny router uczyniłby interaktywnymi także `Login`,
`Register` i `Logout`, a `HttpContext.SignInAsync` wymaga odpowiedzi, która się jeszcze nie
zaczęła — F-02 zapisało to jako odkrycie przed implementacją. Osobna zmiana, nie poprawka tutaj.

### Do rozważenia poza tą zmianą (advisor Supabase, 2026-08-02)

Zauważone przy weryfikacji Fazy 4, oba pochodzą sprzed tej zmiany:

- `public.handle_new_user()` jest wywoływalna przez `anon` i `authenticated` przez
  `/rest/v1/rpc/handle_new_user`. Jest `SECURITY DEFINER`, więc wstawiałaby do `profiles` z
  pominięciem RLS. Realnie niewykorzystywalna, bo PostgreSQL odrzuca bezpośrednie wywołanie funkcji
  `RETURNS trigger`. Domyka to `REVOKE EXECUTE ... FROM anon, authenticated` w migracji.
- Ochrona przed wyciekłymi hasłami (HaveIBeenPwned) wyłączona — ustawienie w panelu Supabase.
Alternatywa, która przywróciłaby unieważnianie: nieść i odnawiać token GoTrue, żeby to Supabase
było autorytetem sesji — cofa świadomą decyzję F-02 („No Supabase session persistence"), więc
odłożone jako osobna zmiana, nie faza tej.

### Świadomie poza zakresem tej zmiany

- Rate limiting logowania i rejestracji (osobne znalezisko z review F-02; realny problem — limity
  GoTrue liczą się per-IP, a serwer jest jedynym klientem, więc jeden nadużywający klient blokuje
  logowanie wszystkim). Wydzielone, bo dotyczy warstwy HTTP, nie sesji.
- Szyfrowanie key ringu DataProtection — ryzyko zaakceptowane świadomie w
  `context/archive/2026-07-27-email-password-auth/change.md:14-26`, do rewizji przed prawdziwymi użytkownikami.
- Konfiguracja bramki `/health/ready` po stronie Coolify (pozycja F3 z review F-01) — sprawa
  deploymentu, nie sesji.
- `Trust Server Certificate=true` — świadomie pominięte w triage F-01 (F8).
