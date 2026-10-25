using System.Windows;
using TelegramControlStudio.Core.Telegram;
using TelegramControlStudio.Infrastructure.Telegram;

namespace TelegramControlStudio.App;

public partial class AuthenticationWizardWindow : Window
{
    private readonly long _accountId;
    private readonly AuthorizationStateMachine _stateMachine;
    private readonly TdLibAccountAuthorizationCoordinator _coordinator;

    public AuthenticationWizardWindow(long accountId, AuthorizationStateMachine stateMachine, TdLibAccountAuthorizationCoordinator coordinator)
    {
        InitializeComponent();
        _accountId = accountId;
        _stateMachine = stateMachine;
        _coordinator = coordinator;
        _stateMachine.StateChanged += StateMachine_StateChanged;
        UpdateState();
    }

    private void StateMachine_StateChanged(object? sender, AuthorizationState e) => Dispatcher.Invoke(UpdateState);

    private void UpdateState()
    {
        StateText.Text = _stateMachine.UserFacingStep;
        SubmitButton.IsEnabled = _stateMachine.CanSubmitPhoneNumber || _stateMachine.CanSubmitEmailAddress || _stateMachine.CanSubmitEmailCode || _stateMachine.CanSubmitCode || _stateMachine.CanSubmitPassword || _stateMachine.CanRegister;
        SecretInputBox.Clear();
        PhoneInputBox.Clear();
        RegistrationFirstNameBox.Clear();
        RegistrationLastNameBox.Clear();
        PhoneInputBox.Visibility = _stateMachine.CanSubmitPhoneNumber ? Visibility.Visible : Visibility.Collapsed;
        RegistrationPanel.Visibility = _stateMachine.CanRegister ? Visibility.Visible : Visibility.Collapsed;
        SecretInputBox.Visibility = _stateMachine.CanSubmitPhoneNumber || _stateMachine.CanRegister ? Visibility.Collapsed : Visibility.Visible;
        SubmitButton.Content = _stateMachine.CanSubmitPhoneNumber ? "เริ่มด้วยเบอร์โทรศัพท์" : _stateMachine.CanRegister ? "ลงทะเบียนบัญชี" : "ส่งข้อมูลขั้นตอนนี้";
    }

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var value = _stateMachine.CanSubmitPhoneNumber ? PhoneInputBox.Text.Trim() : SecretInputBox.Password.Trim();
            if (_stateMachine.CanRegister)
            {
                if (string.IsNullOrWhiteSpace(RegistrationFirstNameBox.Text)) { NoticeText.Text = "กรุณากรอกชื่อ"; return; }
                await _coordinator.SubmitRegistrationAsync(_accountId, RegistrationFirstNameBox.Text.Trim(), RegistrationLastNameBox.Text.Trim());
                NoticeText.Text = "ส่งข้อมูลลงทะเบียนแล้ว กำลังรอสถานะจาก Telegram";
                return;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                NoticeText.Text = "กรุณากรอกข้อมูลของขั้นตอนนี้";
                return;
            }

            if (_stateMachine.CanSubmitPhoneNumber)
                await _coordinator.SubmitPhoneNumberAsync(_accountId, value);
            else if (_stateMachine.CanSubmitEmailAddress)
                await _coordinator.SubmitEmailAddressAsync(_accountId, value);
            else if (_stateMachine.CanSubmitEmailCode)
                await _coordinator.SubmitEmailCodeAsync(_accountId, value);
            else if (_stateMachine.CanSubmitCode)
                await _coordinator.SubmitCodeAsync(_accountId, value);
            else if (_stateMachine.CanSubmitPassword)
                await _coordinator.SubmitPasswordAsync(_accountId, value);
            NoticeText.Text = "ส่งคำขอแล้ว กำลังรอสถานะจาก Telegram";
        }
        catch (Exception exception)
        {
            NoticeText.Text = $"ยังส่งข้อมูลไม่ได้: {exception.Message}";
        }
        finally
        {
            SecretInputBox.Clear();
            PhoneInputBox.Clear();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _stateMachine.StateChanged -= StateMachine_StateChanged;
        Close();
    }
}
