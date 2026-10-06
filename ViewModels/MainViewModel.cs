using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TransactionalMemoryMAUI.Models;

namespace TransactionalMemoryMAUI.ViewModels;

public enum DemoType
{
    SingleAccountCas,
    TransferStm
}

public enum WorkerState
{
    Idle,
    Reading,
    Computing,
    Committing,
    Conflict,
    Success
}

/// <summary>
/// Визуальное состояние одного "потока" для отрисовки на GraphicsView.
/// </summary>
public class WorkerVisual
{
    public int Id { get; init; }
    public volatile WorkerState State = WorkerState.Idle;
    public int Attempts;
    public int TargetAccount; // 0 -> B->A, 1 -> A->B (для демо 2)
    public double Angle; // для позиционирования
}

public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #region Настройки

    private double _workerCount = 16;
    public double WorkerCount
    {
        get => _workerCount;
        set
        {
            if (Math.Abs(_workerCount - value) < 0.5) return;
            _workerCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WorkerCountInt));
            if (!IsRunning) ResetWorkers();
        }
    }

    public int WorkerCountInt => Math.Max(2, (int)WorkerCount);

    private double _depositAmount = 10;
    public double DepositAmount
    {
        get => _depositAmount;
        set
        {
            if (Math.Abs(_depositAmount - value) < 0.5) return;
            _depositAmount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DepositAmountInt));
        }
    }

    public int DepositAmountInt => (int)DepositAmount;

    private double _transferAmount = 20;
    public double TransferAmount
    {
        get => _transferAmount;
        set
        {
            if (Math.Abs(_transferAmount - value) < 0.5) return;
            _transferAmount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransferAmountInt));
        }
    }

    public int TransferAmountInt => (int)TransferAmount;

    private DemoType _selectedDemo = DemoType.SingleAccountCas;
    public DemoType SelectedDemo
    {
        get => _selectedDemo;
        set
        {
            if (_selectedDemo == value) return;
            _selectedDemo = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSingleDemo));
            OnPropertyChanged(nameof(IsTransferDemo));
            OnPropertyChanged(nameof(ConflictsText));
            OnPropertyChanged(nameof(InfoText));
            ResetAll();
        }
    }

    public bool IsSingleDemo => SelectedDemo == DemoType.SingleAccountCas;
    public bool IsTransferDemo => SelectedDemo == DemoType.TransferStm;

    public string InfoText => SelectedDemo == DemoType.SingleAccountCas
        ? "Демо 1: N потоков параллельно пополняют один счёт через Interlocked.CompareExchange (CAS). Без lock, оптимистично."
        : "Демо 2: N потоков параллельно переводят деньги между счетами A и B. STM с MVCC сохраняет сумму A+B.";

    #endregion

    public WorkerVisual[] Workers { get; private set; } = Array.Empty<WorkerVisual>();

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotRunning));
            (StartCommand as Command)?.ChangeCanExecute();
            (ResetCommand as Command)?.ChangeCanExecute();
        }
    }

    public bool IsNotRunning => !_isRunning;

    #region Лог

    private readonly object _logLock = new();
    private readonly List<string> _logLines = new();
    private string _logText = "Готов к запуску демонстрации...\nВыберите тип демо и нажмите 'Запустить'.";
    public string LogText
    {
        get => _logText;
        private set { _logText = value; OnPropertyChanged(); }
    }

    private void AppendLog(string line)
    {
        lock (_logLock)
        {
            _logLines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
            if (_logLines.Count > 400) _logLines.RemoveAt(0);
        }
    }

    /// <summary> Вызывается из UI-таймера для обновления графики/лога/статистики. </summary>
    public void RefreshStats()
    {
        OnPropertyChanged(nameof(SingleAccountBalance));
        OnPropertyChanged(nameof(AccountABalance));
        OnPropertyChanged(nameof(AccountBBalance));
        OnPropertyChanged(nameof(ConflictsText));
        OnPropertyChanged(nameof(SumText));
        lock (_logLock)
        {
            var snapshot = string.Join(Environment.NewLine, Enumerable.Reverse(_logLines));
            if (snapshot != _logText) LogText = snapshot;
        }
    }

    #endregion

    #region Демо 1: CAS

    private CasAccount _casAccount = new();
    public int SingleAccountBalance => _casAccount.Balance;
    private int _totalConflictsDemo1;
    private int _expectedBalanceDemo1;

    #endregion

    #region Демо 2: STM (MVCC)

    private TVar<int> _accountA = new(1000);
    private TVar<int> _accountB = new(500);
    public int AccountABalance => _accountA.Read().Value;
    public int AccountBBalance => _accountB.Read().Value;
    public string SumText => $"A+B = {AccountABalance + AccountBBalance} (инвариант)";
    private int _totalConflictsDemo2;
    private int _sumBefore;

    #endregion

    public string ConflictsText => IsSingleDemo
        ? $"Конфликтов CAS (retry): {_totalConflictsDemo1} | Ожидалось: {_expectedBalanceDemo1} | Факт: {SingleAccountBalance}"
        : $"Конфликтов STM (retry): {_totalConflictsDemo2} | {SumText}";

    public ICommand StartCommand { get; }
    public ICommand ResetCommand { get; }

    public MainViewModel()
    {
        StartCommand = new Command(async () => await RunSelectedDemoAsync(), () => IsNotRunning);
        ResetCommand = new Command(ResetAll, () => IsNotRunning);
        ResetWorkers();
    }

    private void ResetWorkers()
    {
        int n = WorkerCountInt;
        Workers = Enumerable.Range(0, n).Select(i => new WorkerVisual
        {
            Id = i,
            Angle = 2 * Math.PI * i / n - Math.PI / 2
        }).ToArray();
        OnPropertyChanged(nameof(Workers));
    }

    private void ResetAll()
    {
        _casAccount = new CasAccount();
        _accountA = new TVar<int>(1000);
        _accountB = new TVar<int>(500);
        _totalConflictsDemo1 = 0;
        _totalConflictsDemo2 = 0;
        _expectedBalanceDemo1 = 0;
        _sumBefore = 1500;
        ResetWorkers();
        lock (_logLock) { _logLines.Clear(); }
        LogText = SelectedDemo == DemoType.SingleAccountCas
            ? "Готов к Демо 1 (CAS). N потоков будут пополнять один счёт."
            : "Готов к Демо 2 (STM). N потоков будут переводить деньги между A и B с сохранением суммы.";
        RefreshStats();
    }

    private async Task RunSelectedDemoAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        try
        {
            if (SelectedDemo == DemoType.SingleAccountCas)
                await RunDemo1Async();
            else
                await RunDemo2Async();
        }
        finally
        {
            IsRunning = false;
            RefreshStats();
        }
    }

    private async Task RunDemo1Async()
    {
        _casAccount = new CasAccount();
        _totalConflictsDemo1 = 0;
        ResetWorkers();
        int amount = DepositAmountInt;
        int n = Workers.Length;
        _expectedBalanceDemo1 = n * amount;

        AppendLog("=== ДЕМО 1: CAS / Interlocked.CompareExchange ===");
        AppendLog($"Потоков: {n}, депозит каждого: {amount}, ожидаемый итог: {n * amount}");
        AppendLog("Оптимистичный подход: читаем -> считаем -> CAS, при конфликте retry");

        var tasks = Workers.Select(w => Task.Run(() =>
        {
            w.State = WorkerState.Reading;
            Thread.Sleep(Random.Shared.Next(5, 60));
            w.State = WorkerState.Computing;
            int attempts = _casAccount.Deposit(amount, onConflict: () =>
            {
                w.State = WorkerState.Conflict;
                Interlocked.Increment(ref _totalConflictsDemo1);
                Thread.Sleep(Random.Shared.Next(5, 25));
                w.State = WorkerState.Computing;
            });
            w.Attempts = attempts;
            w.State = WorkerState.Success;
            AppendLog($"Поток {w.Id,2}: +{amount}, попыток CAS: {attempts} {(attempts > 1 ? "(был конфликт)" : "")}");
        })).ToArray();

        await Task.WhenAll(tasks);

        AppendLog($"=== ИТОГ: баланс = {_casAccount.Balance} (ожидалось {n * amount}), retry: {_totalConflictsDemo1} ===");
        AppendLog(_casAccount.Balance == n * amount ? "✓ Корректность подтверждена!" : "✗ Ошибка!");
    }

    private async Task RunDemo2Async()
    {
        _accountA = new TVar<int>(1000);
        _accountB = new TVar<int>(500);
        _totalConflictsDemo2 = 0;
        ResetWorkers();
        int amount = TransferAmountInt;
        int n = Workers.Length;
        _sumBefore = _accountA.Read().Value + _accountB.Read().Value;

        AppendLog("=== ДЕМО 2: STM-перевод (MVCC: версии + commit-time validation) ===");
        AppendLog($"Потоков: {n}, сумма перевода: {amount}, A0={_accountA.Read().Value}, B0={_accountB.Read().Value}, сумма={_sumBefore}");
        AppendLog("Алгоритм: снимок (Value, Version) -> локальные вычисления -> валидация версий -> коммит или откат");

        var tasks = Workers.Select(w => Task.Run(() =>
        {
            bool aToB = w.Id % 2 == 0;
            w.TargetAccount = aToB ? 1 : 0;
            w.State = WorkerState.Reading;
            Thread.Sleep(Random.Shared.Next(5, 60));
            w.State = WorkerState.Computing;

            var from = aToB ? _accountA : _accountB;
            var to = aToB ? _accountB : _accountA;

            var result = StmEngine.Transfer(from, to, amount, onConflict: () =>
            {
                w.State = WorkerState.Conflict;
                Interlocked.Increment(ref _totalConflictsDemo2);
                Thread.Sleep(Random.Shared.Next(2, 15));
                w.State = WorkerState.Computing;
            });

            w.Attempts = result.Attempts;
            w.State = result.Success ? WorkerState.Success : WorkerState.Conflict;

            if (result.Success)
                AppendLog($"Поток {w.Id,2}: перевод {(aToB ? "A→B" : "B→A")} {amount}, попыток: {result.Attempts}");
            else
                AppendLog($"Поток {w.Id,2}: ОТКАЗ — {result.ErrorMessage}");
        })).ToArray();

        await Task.WhenAll(tasks);

        int sumAfter = _accountA.Read().Value + _accountB.Read().Value;
        AppendLog($"=== ИТОГ: A={_accountA.Read().Value}, B={_accountB.Read().Value}, сумма до={_sumBefore}, после={sumAfter}, retry: {_totalConflictsDemo2} ===");
        AppendLog(sumAfter == _sumBefore ? "✓ Инвариант суммы сохранён! Транзакции атомарны." : "✗ Инвариант нарушен!");
    }
}
