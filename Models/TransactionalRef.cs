namespace TransactionalMemoryMAUI.Models;

/// <summary>
/// Обёртка над значением, поддерживающая транзакционность — учебный пример,
/// как в методичке "Транзакционная память" (Transactional<int>).
/// В реальной STM здесь была бы логика версионирования и журналирования.
/// </summary>
public class TransactionalRef<T>
{
    public T Value { get; set; }

    public TransactionalRef(T initialValue)
    {
        Value = initialValue;
    }
}

/// <summary>
/// Пример модели банка из задания (Пример функционала 2 с Nito.Transactions)
/// Демонстрирует идею TxContext.ExecuteInTransaction
/// </summary>
public class BankModel
{
    public TransactionalRef<int> AccountA = new(1000);
    public TransactionalRef<int> AccountB = new(500);

    // В реальном STM здесь был бы TVar<int> вместо TransactionalRef<int>
    // и логика версионирования из StmEngine.

    public void Transfer(int amount)
    {
        // Запуск транзакции памяти — гипотетический API как в Nito.Transactions
        // TxContext.ExecuteInTransaction(() =>
        // {
        //     AccountA.Value -= amount;
        //     AccountB.Value += amount;
        // });

        // Наша учебная реализация через StmEngine:
        // (в UI используется напрямую StmEngine.Transfer)
    }
}

/// <summary>
/// Классический пример с Account из методички (Пример функционала 1)
/// </summary>
public class Account
{
    private int _balance;

    public int Balance => _balance;

    public Account(int initial = 0) => _balance = initial;

    public void DepositTransactional(int amount)
    {
        int currentBalance;
        int newBalance;

        do
        {
            // 1. Читаем текущее состояние ("начало транзакции")
            currentBalance = _balance;

            // 2. Вычисляем новое значение локально
            newBalance = currentBalance + amount;

            // Immitate some heavy work / context switch
            Thread.SpinWait(10);

            // 3. Пытаемся зафиксировать (Commit).
            // CompareExchange проверяет: если _balance все еще равен currentBalance,
            // то записывает newBalance. Возвращает то значение, которое БЫЛО в _balance.
        }
        // Если вернулось НЕ currentBalance, значит другой поток успел изменить _balance.
        // Транзакция "откатывается" и цикл идет на новый круг (Retry).
        while (Interlocked.CompareExchange(ref _balance, newBalance, currentBalance) != currentBalance);
    }

    public static async Task Demo100Threads()
    {
        var account = new Account();
        Task[] tasks = new Task[100];

        // 100 потоков одновременно пополняют счет на 10
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => account.DepositTransactional(10));
        }

        await Task.WhenAll(tasks);
        Console.WriteLine($"Итоговый баланс: {account.Balance}"); // Всегда 1000
    }
}
