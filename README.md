# BongoCatPatcher

Утилита на базе .NET и Mono.Cecil для статического патчинга сборки `Assembly-CSharp.dll` игры Bongo Cat (Unity 6 Mono).

## Что делает патчер

Модифицирует клиентские методы в `Assembly-CSharp.dll`:
- `BongoCat.SteamItemRuntime.get_IsOwned()` -> возвращает `true` (все предметы считаются полученными).
- `BongoCat.SteamItemRuntime.get_IsHiddenInInventory()` -> возвращает `false` (предметы не скрываются).
- `BongoCat.ItemCollection.IsRevealed(...)` -> возвращает `true` (все скрытые/событийные скины отображаются).

## Использование

1. Убедитесь, что установлены .NET SDK 8+ или 10+.
2. Закройте Bongo Cat.
3. Запустите патчер:
   ```bash
   dotnet run
   ```
4. Утилита автоматически создаст резервные копии и применит изменения к `Assembly-CSharp.dll`.
