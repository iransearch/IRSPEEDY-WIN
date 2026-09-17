using System;
using System.Threading.Tasks;

namespace IRSpeedyVPN.Authentication
{
    public interface IAuthService
    {
        Task<AuthResult> LoginAsync(string username, string password);
    }

    public sealed class AuthResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }

        public static AuthResult Succeeded() => new AuthResult { Success = true, ErrorMessage = string.Empty };
        public static AuthResult Failed(string message) => new AuthResult
        {
            ErrorMessage = string.IsNullOrWhiteSpace(message) ? "ورود به حساب انجام نشد. دوباره تلاش کنید." : message
        };
    }

    // Adapts the existing account/device-limit/renewal flow without replacing its API policy.
    public sealed class DelegateAuthService : IAuthService
    {
        private readonly Func<string, string, Task<AuthResult>> login;
        public DelegateAuthService(Func<string, string, Task<AuthResult>> login)
        {
            this.login = login ?? throw new ArgumentNullException(nameof(login));
        }
        public Task<AuthResult> LoginAsync(string username, string password) => login(username, password);
    }
}
