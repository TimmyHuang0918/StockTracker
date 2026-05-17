using StockTracker.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace StockTracker.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly CapitalApiService _apiService;
        private string _account;
        private string _password;
        private string _statusMessage;
        private bool _isBusy;

        public LoginViewModel(CapitalApiService apiService)
        {
            _apiService = apiService;
            LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => !IsBusy);
        }

        public event Action<CapitalApiService> LoginSucceeded;

        public string Account
        {
            get => _account;
            set
            {
                _account = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        public ICommand LoginCommand { get; }

        private async Task LoginAsync()
        {
            var loginId = Account?.Trim();
            var password = Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(password))
            {
                StatusMessage = "請輸入帳號與密碼";
                return;
            }

            IsBusy = true;
            try
            {
                StatusMessage = "登入中...";
                var connected = await _apiService.LoginAsync(loginId, password);
                if (!connected)
                {
                    StatusMessage = "登入或報價連線失敗，請稍後重試";
                    return;
                }

                StatusMessage = "登入成功";
                LoginSucceeded?.Invoke(_apiService);
            }
            finally
            {
                IsBusy = false;
            }
        }

    }
}
