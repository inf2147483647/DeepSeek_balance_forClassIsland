﻿using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeepSeekBalance.Models;

/// <summary>
/// 一条已配置的 DeepSeek API 密钥。
/// </summary>
public class DeepSeekApi : INotifyPropertyChanged
{
    private string _maskedKey = "";

    /// <summary>
    /// 条目的唯一标识。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 脱敏后的密钥，仅用于界面展示。
    /// </summary>
    public string MaskedKey
    {
        get => _maskedKey;
        set
        {
            if (value == _maskedKey) return;
            _maskedKey = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// DeepSeek API 密钥。
    /// </summary>
    public string ApiKey { get; set; } = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
