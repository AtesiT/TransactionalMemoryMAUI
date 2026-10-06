using TransactionalMemoryMAUI.ViewModels;
using TransactionalMemoryMAUI.Views;

namespace TransactionalMemoryMAUI;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;
    private IDispatcherTimer? _timer;

    public MainPage()
    {
        InitializeComponent();
        _vm = (MainViewModel)BindingContext;
        CanvasView.Drawable = new StmDrawable(_vm);

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(80);
        _timer.Tick += (s, e) =>
        {
            _vm.RefreshStats();
            CanvasView.Invalidate();
        };
        _timer.Start();
    }

    private void OnDemoPickerChanged(object sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            _vm.SelectedDemo = picker.SelectedIndex == 0
                ? DemoType.SingleAccountCas
                : DemoType.TransferStm;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }
}
