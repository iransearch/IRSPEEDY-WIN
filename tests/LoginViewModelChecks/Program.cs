using IRSpeedyVPN.Authentication;
using IRSpeedyVPN.ViewModels;
using System;
using System.Security;
using System.Threading.Tasks;

internal static class Program
{
    private static int assertions;
    private static void Check(bool pass, string message)
    {
        assertions++;
        if (!pass) throw new Exception(message);
    }
    private static void SetPassword(LoginViewModel vm, string text)
    {
        using var value = new SecureString();
        foreach (char c in text) value.AppendChar(c);
        vm.Password = value;
    }
    private static async Task Main()
    {
        int calls = 0, navigations = 0, recovery = 0;
        var completion = new TaskCompletionSource<AuthResult>();
        var service = new DelegateAuthService((u, p) =>
        {
            calls++;
            Check(u == "demo" && p == "demo123", "Credentials must reach the injected service unchanged (username trimmed).");
            return completion.Task;
        });
        using var vm = new LoginViewModel(service, () => navigations++, () => recovery++);
        await vm.LoginAsync();
        Check(calls == 0 && vm.HasUsernameError && vm.HasPasswordError, "Both empty fields must fail before network work.");
        vm.Username = "   ";
        SetPassword(vm, "demo123");
        await vm.LoginAsync();
        Check(vm.HasUsernameError && !vm.HasPasswordError && calls == 0, "Whitespace username must fail independently.");
        vm.Username = " demo ";
        vm.ClearPassword();
        await vm.LoginAsync();
        Check(!vm.HasUsernameError && vm.HasPasswordError && calls == 0, "Missing password must fail independently.");
        SetPassword(vm, "demo123");
        using (var copy = vm.Password) copy.Dispose();
        Check(vm.HasPassword, "Disposing a password copy must not damage the model.");
        vm.TogglePasswordCommand.Execute(null);
        Check(vm.IsPasswordVisible, "Toggle must show the password.");
        var pending = vm.LoginAsync();
        Check(vm.IsLoading && !vm.IsInteractive && !vm.IsPasswordVisible, "Loading must disable inputs and hide the plaintext view.");
        Check(!vm.LoginCommand.CanExecute(null) && !vm.TogglePasswordCommand.CanExecute(null) && !vm.ForgotPasswordCommand.CanExecute(null), "Commands must be disabled while pending.");
        await vm.LoginAsync();
        vm.LoginCommand.Execute(null);
        Check(calls == 1, "Repeated Enter/click must not start duplicate authentication.");
        completion.SetResult(AuthResult.Succeeded());
        await pending;
        Check(navigations == 1 && !vm.IsLoading && !vm.HasPassword && !vm.HasError, "Success must navigate once, clear password and finish loading.");
        vm.ForgotPasswordCommand.Execute(null);
        Check(recovery == 1, "Recovery command must invoke the provided flow.");

        using var stubVm = new LoginViewModel(new StubAuthService(), () => navigations++, () => { });
        stubVm.Username = "demo";
        SetPassword(stubVm, "wrong");
        var rejection = stubVm.LoginAsync();
        Check(stubVm.IsLoading, "The 800ms demo request should be asynchronous.");
        await rejection;
        Check(stubVm.HasError && !stubVm.IsLoading && navigations == 1, "Rejected credentials must never navigate.");
        SetPassword(stubVm, "demo123");
        await stubVm.LoginAsync();
        Check(navigations == 2 && !stubVm.HasError, "Stub must accept demo/demo123.");

        using var broken = new LoginViewModel(new DelegateAuthService((u,p) => throw new Exception("private detail")), () => throw new Exception("must not navigate"), () => throw new Exception("private URL"));
        broken.Username = "user";
        SetPassword(broken, "password");
        await broken.LoginAsync();
        Check(broken.HasError && !broken.ErrorMessage.Contains("private") && !broken.IsLoading, "Service exceptions must reset loading and display a safe error.");
        broken.ForgotPasswordCommand.Execute(null);
        Check(!broken.ErrorMessage.Contains("private"), "Recovery exceptions must be safe.");
        vm.Dispose();
        Check(!vm.LoginCommand.CanExecute(null), "Disposed models must disable login.");
        Console.WriteLine("PASS: " + assertions + " login behavior assertions.");
    }
}
