namespace TransactionalMemoryMAUI.Models;

/// <summary>
/// Упрощённая "транзакционная переменная" - аналог TVar (Haskell STM)
/// или Ref (Clojure/ScalaSTM), реализованная на чистом C#.
/// Подход MVCC: значение хранится вместе с номером версии,
/// вся ячейка (значение + версия) заменяется атомарно одной ссылкой.
/// Это позволяет реализовать изоляцию и валидацию без длительных блокировок.
/// </summary>
public sealed class TVar<T>
{
    private sealed record Cell(T Value, long Version);

    private Cell _cell;

    public TVar(T initial) => _cell = new Cell(initial, 0);

    /// <summary> Снимок значения и версии ("начало транзакции"). </summary>
    public (T Value, long Version) Read()
    {
        var c = Volatile.Read(ref _cell);
        return (c.Value, c.Version);
    }

    /// <summary> Текущее значение без версии (для UI). </summary>
    public T Value => Volatile.Read(ref _cell).Value;

    /// <summary> Текущая версия (для диагностики). </summary>
    public long Version => Volatile.Read(ref _cell).Version;

    /// <summary>
    /// Принудительная установка значения с увеличением версии.
    /// Вызывается только внутри критической секции коммита в StmEngine.
    /// </summary>
    internal void ForceSet(T value, long version) =>
        Volatile.Write(ref _cell, new Cell(value, version));

    public void Reset(T value) => Volatile.Write(ref _cell, new Cell(value, 0));
}
