namespace TransactionalMemoryMAUI.Models;

/// <summary>
/// Пример функционала 1 из задания: симуляция STM через CAS-операции
/// (Interlocked.CompareExchange) без использования lock.
/// Оптимистичная блокировка: читаем -> считаем -> пытаемся зафиксировать,
/// при неудаче - откат вычислений и повтор (retry).
/// </summary>
public sealed class CasAccount
{
    private int _balance;

    public int Balance => Volatile.Read(ref _balance);

    public CasAccount(int initial = 0)
    {
        _balance = initial;
    }

    /// <summary>
    /// Пополнение счёта с использованием CAS. Возвращает количество попыток.
    /// </summary>
    public int Deposit(int amount, Action? onConflict = null)
    {
        int attempts = 0;
        while (true)
        {
            attempts++;
            int current = _balance;                          // 1. Начало "транзакции" - снимок
            int updated = current + amount;                  // 2. Вычисление нового значения локально
            Thread.SpinWait(Random.Shared.Next(50, 300));    // Имитация полезной работы / переключения контекста

            // 3. Попытка commit. Если _balance не изменился - фиксируем.
            // CompareExchange возвращает то, что БЫЛО в _balance до попытки.
            if (Interlocked.CompareExchange(ref _balance, updated, current) == current)
                return attempts;

            // Конфликт: другой поток успел изменить баланс. Откат и retry.
            onConflict?.Invoke();
        }
    }

    public void Reset(int value = 0) => Volatile.Write(ref _balance, value);
}
