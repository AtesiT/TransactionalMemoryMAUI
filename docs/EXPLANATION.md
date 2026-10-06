# Пояснительная записка: Транзакционная память в .NET MAUI

## Введение

Транзакционная память (Transactional Memory, TM) — технология синхронизации конкурентных потоков, упрощающая параллельное программирование выделением групп инструкций в атомарные транзакции. Конкурентные потоки работают параллельно, пока не начинают модифицировать один и тот же участок памяти.

Подход называется **оптимистичным**: мы считаем, что потоки работают независимо и редко конфликтуют. В отличие от пессимистичного подхода с `lock`, где потоки всегда выстраиваются в очередь.

## История

- 1980-е: идея переноса транзакций из БД в параллельное программирование (Herlihy, Rajwar, Shavit)
- 1990-е: первые программные реализации (STM)
- 2000-е: аппаратные реализации (HTM) — Sun Rock, IBM BlueGene/Q, Intel Haswell TSX (HLE и RTM)
- В .NET: экспериментальный проект MSR STM.NET был закрыт, из коробки STM нет

## Программные реализации STM (из методички)

### Clojure
Единственный язык, ядро которого поддерживает STM. Конструкции: `ref` и `dosync`. Подход MVCC — хранение множественных логических версий данных.

### Haskell
Библиотека STM, типы `TVar a`, операции `readTVar`, `writeTVar`, `atomically`. Проверка корректности на этапе компиляции.

### Scala (ScalaSTM)
Ячейка `Ref`, используется в Akka.

### C/C++ (GCC 4.7+)
Ключевые слова `__transaction_atomic`, библиотека `libitm`.

## Реализация в данном проекте

Поскольку в официальном .NET нет встроенной STM, мы реализовали два учебных механизма:

### 1. CAS через Interlocked.CompareExchange (Пример 1 из задания)

```csharp
class Account 
{
    private int _balance;
    public void DepositTransactional(int amount) 
    {
        int currentBalance, newBalance;
        do
        {
            currentBalance = _balance;          // начало транзакции
            newBalance = currentBalance + amount;
            Thread.SpinWait(10);
        }
        while (Interlocked.CompareExchange(ref _balance, newBalance, currentBalance) != currentBalance);
    }
}
```

**Как работает:**
- `Interlocked.CompareExchange(ref _balance, newBalance, currentBalance)` — атомарно проверяет, что `_balance` всё ещё равен `currentBalance`, и если да, записывает `newBalance`
- Возвращает то, что БЫЛО в `_balance` до операции
- Если вернулось не `currentBalance` — другой поток успел изменить баланс, цикл повторяется (retry)
- Это **оптимистичная блокировка** без `lock`

**Где в проекте:** `Models/CasAccount.cs`

**Тест из задания:** 100 потоков пополняют счёт на 10 — итог всегда 1000, без потерь.

### 2. Учебный STM-движок MVCC (Пример 2 из задания)

Идея из `Nito.Transactions`:

```csharp
public class BankModel 
{
    public Transactional<int> AccountA = new(100);
    public Transactional<int> AccountB = new(50);
    public void Transfer(int amount) 
    {
        TxContext.ExecuteInTransaction(() => 
        {
            AccountA.Value -= amount;
            AccountB.Value += amount;
        });
    }
}
```

В .NET такой библиотеки нет, поэтому мы написали аналог:

**TVar<T>** — транзакционная переменная, аналог `TVar` в Haskell / `Ref` в Clojure:

```csharp
public sealed class TVar<T>
{
    private sealed record Cell(T Value, long Version);
    private Cell _cell;
    public (T Value, long Version) Read() => ... // снимок
    internal void ForceSet(T value, long version) => ... // только в коммите
}
```

- Хранит значение вместе с версией (MVCC)
- Вся ячейка заменяется атомарно одной ссылкой
- `Read()` — изоляция, снимок на момент начала транзакции

**StmEngine** — движок транзакций:

```csharp
public static TxResult Transfer(TVar<int> from, TVar<int> to, int amount)
{
    while (true)
    {
        var (fromVal, fromVer) = from.Read();
        var (toVal, toVer) = to.Read();
        // локальные вычисления
        lock (CommitLock) // короткая блокировка только на валидацию+коммит
        {
            if (curFromVer == fromVer && curToVer == toVer)
            {
                from.ForceSet(newFrom, fromVer + 1);
                to.ForceSet(newTo, toVer + 1);
                return Ok();
            }
        }
        // конфликт -> retry
    }
}
```

Алгоритм оптимистичной STM:
1. Чтение и запись в локальный журнал (изолированно)
2. Проверка конфликтов (Validation) — сравнение версий
3. Фиксация (Commit) или Откат (Rollback) + Retry

**Где в проекте:** `Models/TVar.cs`, `Models/StmEngine.cs`

**Инвариант:** сумма A+B сохраняется при любом количестве параллельных переводов.

## Сравнение: STM vs lock

| Критерий | lock (пессимистичный) | STM (оптимистичный) |
|---|---|---|
| Deadlocks | Возможны | Невозможны |
| При отсутствии конфликтов | Тратит ресурсы на монитор | Максимально быстро |
| При высокой конкуренции | Потоки блокируются в очереди | Потоки перезапускаются (CPU spin) |
| Сложность масштабирования | Очень сложно | Легко, чистый код |
| Побочные эффекты | Можно | Нельзя откатить (Email, файл) |
| Накладные расходы | Монитор | Логи чтений/записей, GC |

## Почему STM не стала стандартом в .NET

1. **Откат побочных эффектов**: Email, лог в файл нельзя откатить
2. **Накладные расходы**: логи для каждого потока, нагрузка на GC
3. **Альтернативы**: `ConcurrentDictionary`, акторы (`Akka.NET`), каналы (`Channels`), Event Sourcing / Sagas (`MassTransit`)

## Визуализация в MAUI

Для лабораторной работы требуется "маневр для визуального показа с графикой при параллелизме желательно в мобильном приложении".

Реализовано:

- **GraphicsView** + `IDrawable` — кастомная отрисовка
- Потоки = кружки вокруг счёта (Демо 1) или между счетами (Демо 2)
- Цвет = состояние:
  - Серый — Idle
  - Голубой — Reading (чтение снимка)
  - Оранжевый — Computing (вычисление)
  - Красный — Conflict / Retry
  - Зелёный — Success / Commit
- Линии от потоков к счетам
- Анимация: вращение активных потоков
- Таймер 80мс обновляет графику и статистику
- Лог событий с временными метками
- Слайдеры для количества потоков и сумм
- Счётчики конфликтов

**Файлы:** `Views/StmDrawable.cs`, `ViewModels/MainViewModel.cs`, `MainPage.xaml`

## Запуск и проверка

При нажатии "Запустить" в Демо 1 N потоков параллельно атакуют один `CasAccount` — видно, как часть кружков мигает красным (конфликт CAS) и повторяет попытку, итоговый баланс всегда корректен (N × amount).

В Демо 2 потоки параллельно переводят деньги между A и B в обе стороны — сумма всегда сохраняется, конфликтные транзакции откатываются и перезапускаются.

## Выводы

Транзакционная память — жизнеспособная альтернатива блокировкам, упрощающая параллельное программирование. В .NET её можно симулировать через CAS или написать учебный MVCC-движок. В MAUI это отлично визуализируется графикой и логами.

Для продакшена в .NET рекомендуются: неизменяемые структуры, `Concurrent*`, каналы, акторы, Event Sourcing.

## Ссылки

- Task/Tranzaktsionnaya_Pamyat.pdf — статья с Хабра про TM
- Task/Laboratornaya_Rabota_1.pdf — задание
- Task/maui_app.pdf — готовое решение от Claude (использовано как основа, но переработано)
- https://github.com/JIghtuse/tm-experiments — примеры TM на разных языках
- Intel TSX: HLE (Hardware Lock Elision) и RTM (Restricted Transactional Memory)
