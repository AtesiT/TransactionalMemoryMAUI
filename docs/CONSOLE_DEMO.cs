// Консольный демо-тест без MAUI — можно запустить как `dotnet run` в отдельной папке
// Демонстрирует корректность обоих подходов
// Для проверки логики в этой среде см. simulate_stm.py

using System;
using System.Threading;
using System.Threading.Tasks;
using TransactionalMemoryMAUI.Models;

class ConsoleDemo
{
    static async Task Main()
    {
        Console.WriteLine("=== Демо 1: CAS Account ===");
        var account = new CasAccount();
        var tasks = new Task[100];
        for (int i = 0; i < 100; i++)
            tasks[i] = Task.Run(() => account.Deposit(10));
        await Task.WhenAll(tasks);
        Console.WriteLine($"Итоговый баланс: {account.Balance} (ожидалось 1000) - {(account.Balance == 1000 ? "OK" : "FAIL")}");

        Console.WriteLine("\n=== Демо 2: STM Transfer ===");
        var accA = new TVar<int>(1000);
        var accB = new TVar<int>(500);
        int conflicts = 0;
        var tasks2 = new Task[20];
        for (int i = 0; i < 20; i++)
        {
            int id = i;
            tasks2[i] = Task.Run(() =>
            {
                bool aToB = id % 2 == 0;
                var from = aToB ? accA : accB;
                var to = aToB ? accB : accA;
                var result = StmEngine.Transfer(from, to, 20, () => Interlocked.Increment(ref conflicts));
                Console.WriteLine($"Поток {id}: {(aToB ? "A->B" : "B->A")} попыток {result.Attempts} {(result.Success ? "OK" : "FAIL")}");
            });
        }
        await Task.WhenAll(tasks2);
        int sum = accA.Read().Value + accB.Read().Value;
        Console.WriteLine($"Итог: A={accA.Read().Value} B={accB.Read().Value} сумма={sum} (ожидалось 1500) - {(sum == 1500 ? "OK" : "FAIL")}");
        Console.WriteLine($"Конфликтов: {conflicts}");
    }
}
