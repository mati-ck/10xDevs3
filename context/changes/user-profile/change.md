---
change_id: user-profile
title: Profil użytkownika — nazwa wyświetlana, zmiana hasła, usunięcie konta
status: planned
created: 2026-09-08
updated: 2026-09-08
---

## Notes

- **Nie pochodzi z roadmapy ani z PRD.** Żadna pozycja w `context/foundation/roadmap.md` nie ma `Change ID: user-profile`, a PRD nie wymienia profilu użytkownika. To zmiana spoza pierwotnego zakresu MVP — roadmapa nie jest przez nią modyfikowana.
- Zakres ustalony 2026-09-08: nazwa wyświetlana (podgląd + edycja), zmiana hasła, usunięcie konta. **Poza zakresem:** statystyki użycia (licznik generowań, liczba notatek) — świadomie odrzucone.
- Tabela `profiles` istnieje od F-01 (`persistence-baseline`) i do dziś **nie jest używana przez żaden kod aplikacji** — poza testami. Wiersz dla każdego konta gwarantuje trigger `on_auth_user_created` (`Migrations/20260727200717_AddHandleNewUserTrigger.cs`), więc strona profilu nigdy nie musi obsługiwać przypadku „brak wiersza".
- Zweryfikowane 2026-09-08 zapytaniem do bazy: rola `postgres`, na której działa aplikacja, ma uprawnienie `DELETE` na `auth.users`. Usunięcie konta nie wymaga zatem klucza `service_role` — kasowanie idzie po istniejącym połączeniu, a kaskady FK (`ON DELETE CASCADE` na wszystkich pięciu tabelach) robią resztę.
- Znaleziony przy okazji błąd zastany: `Components/Pages/Account/Register.razor` reklamuje hasło do 100 znaków, a GoTrue przyjmuje maksymalnie **72 bajty** (bcrypt). Hasło 73–100 znaków ląduje w `AuthFailureReason.Unavailable` i użytkownik czyta „Nie udało się utworzyć konta". Naprawiane w fazie 1 — dokładnie wzorzec z `lessons.md` („An advertised limit must be one every layer beneath it can carry", w tym uwaga o bajtach vs znakach).
- Ograniczenie przyjęte świadomie: zmiana hasła **nie unieważnia** ciasteczek już wydanych. Ciasteczko jest samodzielne i podpisane DataProtection, nie ma magazynu sesji po stronie serwera; jedyną istniejącą granicą jest `AuthCookie.SessionCap` (30 dni). „Wyloguj wszędzie" wymagałoby magazynu unieważnień i jest poza zakresem.
- Usunięcie konta kasuje kaskadowo `note_events` — czyli wkład tego konta w metrykę 75% akceptacji z PRD. Utrzymane świadomie: to dane użytkownika, a obietnica usunięcia ma być prawdziwa. Skutek: metryka liczy się wyłącznie po kontach, które przetrwały.

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->
