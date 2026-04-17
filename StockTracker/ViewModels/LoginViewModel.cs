using StockTracker.Services;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace StockTracker.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly FakeCapitalApiService _apiService;
        private string _account;
        private string _password;
        private string _statusMessage;
        private bool _isBusy;

        public LoginViewModel(FakeCapitalApiService apiService)
        {
            _apiService = apiService;
            LoginCommand = new RelayCommand(async _ => await LoginAsync(), _ => !IsBusy);
        }

        public event Action<FakeCapitalApiService> LoginSucceeded;

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
            IsBusy = true;
            StatusMessage = "正在登入群益 API...";
            try
            {
                var success = await _apiService.LoginAsync(Account, Password);
                if (success)
                {
                    StatusMessage = "登入成功";
                    LoginSucceeded?.Invoke(_apiService);
                }
                else
                {
                    StatusMessage = "登入失敗，請檢查帳號密碼";
                }
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
