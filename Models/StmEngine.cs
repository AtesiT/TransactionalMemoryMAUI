namespace TransactionalMemoryMAUI.Models;

/// <summary>
/// Результат выполнения транзакции
/// </summary>
public readonly struct TxResult
{
    public bool Success { get; }
    public int Attempts { get; }
    public string? ErrorMessage { get; }

    private TxResult(bool success, int attempts, string? error)
    {
        Success = success;
        Attempts = attempts;
        ErrorMessage = error;
    }

    public static TxResult Ok(int attempts) => new(true, attempts, null);
    public static TxResult Fail(int attempts, string error) => new(false, attempts, error);
}

/// <summary>
/// Пример функционала 2 из задания: объединение изменений нескольких
/// связанных транзакционных переменных (TVar) в одну атомарную транзакцию
/// с автоматическим откатом (retry) при конфликте — упрощённый аналог
/// Nito.Transactions / TxContext.ExecuteInTransaction.
///
/// Алгоритм оптимистичной транзакционной памяти:
///  1. Снимок (Value, Version) для каждой переменной — "начало транзакции".
///  2. Вычисление новых значений локально, вне критической секции.
///  3. Валидация + Commit под короткой общей блокировкой: если версии
///     не изменились с момента чтения — обе переменные атомарно
///     обновляются; иначе — конфликт, полный перезапуск транзакции.
///
/// Плюсы:
///  - Нет deadlocks (в отличие от lock)
///  - Высокий параллелизм при низкой конкуренции
///  - Сохранение инвариантов (сумма A+B постоянна)
/// Минусы:
///  - Retry при высокой конкуренции
///  - Невозможность отката побочных эффектов (I/O, Email)
/// </summary>
public static class StmEngine
{
    // Короткая блокировка только на фазе валидации+коммита (commit lock)
    // В реальных STM это делается через глобальный clock или TL2.
    private static readonly object CommitLock = new();

    /// <summary>
    /// Атомарный перевод денег между двумя счетами.
    /// Сохраняет инвариант: сумма from + to не меняется.
    /// </summary>
    public static TxResult Transfer(TVar<int> from, TVar<int> to, int amount, Action? onConflict = null)
    {
        int attempts = 0;
        while (true)
        {
            attempts++;

            // 1. Чтение снимков (начало транзакции) - изоляция
            var (fromVal, fromVer) = from.Read();
            var (toVal, toVer) = to.Read();

            // Проверка бизнес-инварианта локально
            if (fromVal < amount)
                return TxResult.Fail(attempts, "Недостаточно средств");

            int newFrom = fromVal - amount;
            int newTo = toVal + amount;

            // 2. Имитация "тяжёлой" работы внутри транзакции — именно в этом
            // окне другой поток может успеть закоммититься и вызвать конфликт.
            // Это демонстрирует оптимистичный подход STM.
            Thread.SpinWait(Random.Shared.Next(50, 400));

            // 3. Валидация и коммит под короткой блокировкой
            lock (CommitLock)
            {
                var (curFromVal, curFromVer) = from.Read();
                var (curToVal, curToVer) = to.Read();

                // Проверяем, что версии не изменились с момента чтения
                if (curFromVer == fromVer && curToVer == toVer)
                {
                    // Коммит: атомарно обновляем обе переменные с увеличением версии
                    from.ForceSet(newFrom, fromVer + 1);
                    to.ForceSet(newTo, toVer + 1);
                    return TxResult.Ok(attempts);
                }
            }

            // Валидация не прошла — версия изменилась, кто-то успел
            // закоммитить конфликтующую транзакцию. Откат и retry.
            onConflict?.Invoke();
        }
    }

    /// <summary>
    /// Обобщённый ExecuteInTransaction — аналог TxContext.ExecuteInTransaction
    /// Позволяет выполнить произвольный код в транзакции с автоматическим retry.
    /// </summary>
    public static void ExecuteInTransaction(Action action, Action? onConflict = null)
    {
        while (true)
        {
            try
            {
                action();
                return; // Успешный коммит внутри action должен сам проверить версии
            }
            catch (ConflictException)
            {
                onConflict?.Invoke();
                // Retry
            }
        }
    }
}

/// <summary>
/// Исключение конфликта транзакций — используется для обобщённого API
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException() : base("STM conflict detected") { }
    public ConflictException(string message) : base(message) { }
}
