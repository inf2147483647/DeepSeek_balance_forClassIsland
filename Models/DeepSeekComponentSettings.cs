using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeepSeekBalance.Models;

/// <summary>
/// 主界面组件每个实例独立的设置。
/// </summary>
public class DeepSeekComponentSettings : INotifyPropertyChanged
{
    private string _selectedApiId = "";

    /// <summary>
    /// 要展示的 API 的 <see cref="DeepSeekApi.Id"/>。为空表示未选择。
    /// </summary>
    public string SelectedApiId
    {
        get => _selectedApiId;
        set
        {
            if (value == _selectedApiId) return;
            _selectedApiId = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
