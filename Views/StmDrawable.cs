using Microsoft.Maui.Graphics;
using TransactionalMemoryMAUI.ViewModels;

namespace TransactionalMemoryMAUI.Views;

public class StmDrawable : IDrawable
{
    private readonly MainViewModel _vm;

    public StmDrawable(MainViewModel vm) => _vm = vm;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(dirtyRect);

        if (_vm.IsSingleDemo)
            DrawSingleAccount(canvas, dirtyRect);
        else
            DrawTransfer(canvas, dirtyRect);
    }

    private void DrawSingleAccount(ICanvas canvas, RectF rect)
    {
        var center = new PointF(rect.Center.X, rect.Center.Y);
        const float boxSize = 110;
        var workers = _vm.Workers;
        int n = workers.Length;
        float radius = Math.Min(rect.Width, rect.Height) / 2f - 70;
        if (radius < 40) radius = 40;

        // Рисуем связи и потоки (кружки вокруг счёта)
        for (int i = 0; i < n; i++)
        {
            double angle = workers[i].Angle;
            // Добавляем лёгкую анимацию вращения для активных потоков
            if (workers[i].State is WorkerState.Reading or WorkerState.Computing or WorkerState.Conflict)
                angle += (DateTime.Now.Millisecond / 1000.0) * 0.3;

            float x = center.X + radius * (float)Math.Cos(angle);
            float y = center.Y + radius * (float)Math.Sin(angle);

            var color = ColorForState(workers[i].State);

            // Линия к центру
            canvas.StrokeColor = color.WithAlpha(0.35f);
            canvas.StrokeSize = workers[i].State == WorkerState.Idle ? 1 : 2;
            canvas.DrawLine(x, y, center.X, center.Y);

            // Кружок потока
            canvas.FillColor = color;
            canvas.FillCircle(x, y, 15);

            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 1;
            canvas.DrawCircle(x, y, 15);

            // ID потока
            canvas.FontColor = Colors.White;
            canvas.FontSize = 10;
            canvas.DrawString(workers[i].Id.ToString(), x - 15, y - 10, 30, 20,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            // Количество попыток
            if (workers[i].Attempts > 1)
            {
                canvas.FontColor = Colors.Black;
                canvas.FontSize = 9;
                canvas.DrawString($"{workers[i].Attempts}", x - 15, y + 12, 30, 12,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        // Центральный счёт
        canvas.FillColor = Color.FromArgb("#1976D2");
        canvas.FillRoundedRectangle(center.X - boxSize / 2, center.Y - boxSize / 2, boxSize, boxSize, 16);

        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 1.5f;
        canvas.DrawRoundedRectangle(center.X - boxSize / 2, center.Y - boxSize / 2, boxSize, boxSize, 16);

        canvas.FontColor = Colors.White;
        canvas.FontSize = 22;
        canvas.DrawString($"{_vm.SingleAccountBalance}", center.X - boxSize / 2, center.Y - 20, boxSize, 30,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.FontSize = 12;
        canvas.DrawString("Счёт (CAS)", center.X - boxSize / 2, center.Y + 10, boxSize, 20,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        DrawLegend(canvas, rect);
    }

    private void DrawTransfer(ICanvas canvas, RectF rect)
    {
        float boxW = 100, boxH = 90;
        var aCenter = new PointF(rect.Width * 0.25f, rect.Center.Y);
        var bCenter = new PointF(rect.Width * 0.75f, rect.Center.Y);

        // Счёт A
        canvas.FillColor = Color.FromArgb("#388E3C");
        canvas.FillRoundedRectangle(aCenter.X - boxW / 2, aCenter.Y - boxH / 2, boxW, boxH, 14);
        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(aCenter.X - boxW / 2, aCenter.Y - boxH / 2, boxW, boxH, 14);

        canvas.FontColor = Colors.White;
        canvas.FontSize = 14;
        canvas.DrawString($"A\n{_vm.AccountABalance}", aCenter.X - boxW / 2, aCenter.Y - boxH / 2, boxW, boxH,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        // Счёт B
        canvas.FillColor = Color.FromArgb("#7B1FA2");
        canvas.FillRoundedRectangle(bCenter.X - boxW / 2, bCenter.Y - boxH / 2, boxW, boxH, 14);
        canvas.DrawRoundedRectangle(bCenter.X - boxW / 2, bCenter.Y - boxH / 2, boxW, boxH, 14);

        canvas.FontColor = Colors.White;
        canvas.DrawString($"B\n{_vm.AccountBBalance}", bCenter.X - boxW / 2, bCenter.Y - boxH / 2, boxW, boxH,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        // Линия между счетами (общая сумма)
        canvas.StrokeColor = Colors.Gray.WithAlpha(0.3f);
        canvas.StrokeSize = 1;
        canvas.StrokeDashPattern = new float[] { 4, 4 };
        canvas.DrawLine(aCenter.X + boxW / 2, aCenter.Y, bCenter.X - boxW / 2, bCenter.Y);
        canvas.StrokeDashPattern = null;

        // Потоки
        var workers = _vm.Workers;
        int n = workers.Length;
        float spanX = bCenter.X - aCenter.X;

        for (int i = 0; i < n; i++)
        {
            float t = (i + 1f) / (n + 1f);
            float x = aCenter.X + spanX * t;
            bool aToB = workers[i].TargetAccount == 1;
            float y = rect.Center.Y + (aToB ? -1 : 1) * (40 + 20 * (i % 3));

            // Лёгкая анимация
            if (workers[i].State is WorkerState.Reading or WorkerState.Computing)
                y += (float)Math.Sin(DateTime.Now.Millisecond / 200.0 + i) * 5;

            var color = ColorForState(workers[i].State);

            // Стрелки к счетам
            canvas.StrokeColor = color.WithAlpha(0.5f);
            canvas.StrokeSize = 2;

            var from = aToB ? aCenter : bCenter;
            var to = aToB ? bCenter : aCenter;

            canvas.DrawLine(x, y, from.X, from.Y);
            canvas.DrawLine(x, y, to.X, to.Y);

            // Кружок потока
            canvas.FillColor = color;
            canvas.FillCircle(x, y, 12);

            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 1;
            canvas.DrawCircle(x, y, 12);

            canvas.FontColor = Colors.White;
            canvas.FontSize = 8;
            canvas.DrawString(workers[i].Id.ToString(), x - 12, y - 8, 24, 16,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        DrawLegend(canvas, rect);
    }

    private void DrawLegend(ICanvas canvas, RectF rect)
    {
        var items = new (WorkerState state, string text)[]
        {
            (WorkerState.Idle, "Ожидание"),
            (WorkerState.Reading, "Чтение снимка"),
            (WorkerState.Computing, "Вычисление"),
            (WorkerState.Conflict, "Конфликт / Retry"),
            (WorkerState.Success, "Коммит (успех)")
        };

        float x = 10, y = rect.Height - 24 * items.Length - 10;
        canvas.FontSize = 11;

        foreach (var (state, text) in items)
        {
            canvas.FillColor = ColorForState(state);
            canvas.FillCircle(x + 6, y + 6, 6);

            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 0.5f;
            canvas.DrawCircle(x + 6, y + 6, 6);

            canvas.FontColor = Colors.Black;
            canvas.DrawString(text, x + 18, y, 200, 18, HorizontalAlignment.Left, VerticalAlignment.Center);
            y += 20;
        }
    }

    private static Color ColorForState(WorkerState s) => s switch
    {
        WorkerState.Idle => Colors.Gray,
        WorkerState.Reading => Colors.DeepSkyBlue,
        WorkerState.Computing => Colors.Orange,
        WorkerState.Committing => Colors.Gold,
        WorkerState.Conflict => Colors.Red,
        WorkerState.Success => Colors.MediumSeaGreen,
        _ => Colors.Gray
    };
}
