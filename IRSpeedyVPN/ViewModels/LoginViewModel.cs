using IRSpeedyVPN.Authentication;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;

namespace IRSpeedyVPN.ViewModels
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public sealed class LoginViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IAuthService authService;
        private readonly Action navigateToMainShell;
        private readonly Action forgotPassword;
        private readonly LoginCommandAction loginCommand;
        private readonly LoginCommandAction toggleCommand;
        private readonly LoginCommandAction forgotCommand;
        private string username = string.Empty;
        private SecureString password = new SecureString();
        private bool passwordVisible;
        private bool rememberMe = true;
        private bool isLoading;
        private bool disposed;
        private string errorMessage = string.Empty;
        private string usernameError = string.Empty;
        private string passwordError = string.Empty;

        public LoginViewModel(IAuthService authService, Action navigateToMainShell, Action forgotPassword)
        {
            this.authService = authService ?? throw new ArgumentNullException(nameof(authService));
            this.navigateToMainShell = navigateToMainShell ?? throw new ArgumentNullException(nameof(navigateToMainShell));
            this.forgotPassword = forgotPassword ?? throw new ArgumentNullException(nameof(forgotPassword));
            loginCommand = new LoginCommandAction(ExecuteLogin, () => IsInteractive);
            toggleCommand = new LoginCommandAction(() => IsPasswordVisible = !IsPasswordVisible, () => IsInteractive);
            forgotCommand = new LoginCommandAction(OpenForgotPassword, () => IsInteractive);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        public ICommand LoginCommand => loginCommand;
        public ICommand TogglePasswordCommand => toggleCommand;
        public ICommand ForgotPasswordCommand => forgotCommand;
        public string Username
        {
            get => username;
            set { username = value ?? string.Empty; Changed(nameof(Username)); UsernameError = string.Empty; ErrorMessage = string.Empty; }
        }

        // Copy-in/copy-out ownership: the view and callers dispose their own copies.
        public SecureString Password
        {
            get => password.Copy();
            set
            {
                var replacement = value == null ? new SecureString() : value.Copy();
                replacement.MakeReadOnly();
                password.Dispose();
                password = replacement;
                Changed(nameof(Password));
                Changed(nameof(HasPassword));
                PasswordError = string.Empty;
                ErrorMessage = string.Empty;
            }
        }
        public bool HasPassword => password.Length > 0;
        public bool IsPasswordVisible
        {
            get => passwordVisible;
            set { if (passwordVisible == value) return; passwordVisible = value; Changed(nameof(IsPasswordVisible)); }
        }
        public bool RememberMe { get => rememberMe; set { rememberMe = value; Changed(nameof(RememberMe)); } }
        public bool IsInteractive => !isLoading && !disposed;
        public bool IsLoading
        {
            get => isLoading;
            private set
            {
                isLoading = value;
                Changed(nameof(IsLoading)); Changed(nameof(IsInteractive));
                loginCommand.RaiseCanExecuteChanged(); toggleCommand.RaiseCanExecuteChanged(); forgotCommand.RaiseCanExecuteChanged();
            }
        }
        public string ErrorMessage
        {
            get => errorMessage;
            set { errorMessage = value ?? string.Empty; Changed(nameof(ErrorMessage)); Changed(nameof(HasError)); }
        }
        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
        public string UsernameError
        {
            get => usernameError;
            private set { usernameError = value; Changed(nameof(UsernameError)); Changed(nameof(HasUsernameError)); }
        }
        public string PasswordError
        {
            get => passwordError;
            private set { passwordError = value; Changed(nameof(PasswordError)); Changed(nameof(HasPasswordError)); }
        }
        public bool HasUsernameError => !string.IsNullOrEmpty(UsernameError);
        public bool HasPasswordError => !string.IsNullOrEmpty(PasswordError);

        private async void ExecuteLogin() { await LoginAsync(); }

        // Exposed separately for deterministic checks; the UI binds LoginCommand.
        public async Task LoginAsync()
        {
            if (!IsInteractive) return;
            ErrorMessage = string.Empty;
            UsernameError = string.IsNullOrWhiteSpace(Username) ? "نام کاربری را وارد کنید." : string.Empty;
            PasswordError = !HasPassword ? "رمز عبور را وارد کنید." : string.Empty;
            if (HasUsernameError || HasPasswordError)
            {
                ErrorMessage = "فیلدهای مشخص‌شده را تکمیل کنید.";
                return;
            }

            IsLoading = true;
            IsPasswordVisible = false;
            try
            {
                AuthResult result;
                var pointer = Marshal.SecureStringToGlobalAllocUnicode(password);
                string requestPassword = null;
                try
                {
                    // The legacy API requires a string. Keep plaintext out of model properties/logs.
                    requestPassword = Marshal.PtrToStringUni(pointer);
                }
                finally { Marshal.ZeroFreeGlobalAllocUnicode(pointer); }
                try { result = await authService.LoginAsync(Username.Trim(), requestPassword); }
                finally { requestPassword = null; }
                if (disposed) return;
                if (result != null && result.Success)
                {
                    navigateToMainShell();
                    ClearPassword();
                }
                else ErrorMessage = result?.ErrorMessage ?? "ورود به حساب انجام نشد. دوباره تلاش کنید.";
            }
            catch (Exception)
            {
                // Never put exception text (possibly containing credentials/URLs) in the UI.
                if (!disposed) ErrorMessage = "ارتباط با سرور برقرار نشد. دوباره تلاش کنید.";
            }
            finally { IsLoading = false; }
        }

        public void ClearPassword() { Password = null; IsPasswordVisible = false; }
        private void OpenForgotPassword()
        {
            try { forgotPassword(); }
            catch { ErrorMessage = "باز کردن صفحه بازیابی رمز عبور ممکن نشد."; }
        }
        private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            password.Dispose();
            IsLoading = false;
        }
    }

    internal sealed class LoginCommandAction : ICommand
    {
        private readonly Action execute;
        private readonly Func<bool> canExecute;
        public LoginCommandAction(Action execute, Func<bool> canExecute) { this.execute = execute; this.canExecute = canExecute; }
        public bool CanExecute(object parameter) => canExecute();
        public void Execute(object parameter) { if (CanExecute(parameter)) execute(); }
        public event EventHandler CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
