using System.Threading.Tasks;

namespace IRSpeedyVPN.Authentication
{
    // Explicitly injected by previews/tests only. Never selected by production startup.
    public sealed class StubAuthService : IAuthService
    {
        public async Task<AuthResult> LoginAsync(string username, string password)
        {
            await Task.Delay(800);
            return username == "demo" && password == "demo123"
                ? AuthResult.Succeeded()
                : AuthResult.Failed("نام کاربری یا رمز عبور صحیح نیست.");
        }
    }
}
