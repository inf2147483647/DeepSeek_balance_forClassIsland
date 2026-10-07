﻿# DeepSeek余额

适用于 [ClassIsland](https://classisland.tech/)（2.0.1.1 及以上）的插件，在主界面组件中实时显示 DeepSeek API 账户余额。

## 功能

- **主界面组件**：显示「余额：`xx.xx`CNY」（精确到 2 位小数），每 0.5 秒刷新一次。
- **余额变动提示**：余额减少时数字闪红，充值时数字闪绿。
- **组件设置**：通过下拉框选择该组件要展示的 API。
- **插件设置**：以列表形式管理 API 密钥，支持添加、校验与删除。
- **深色 / 浅色主题**：文本颜色随应用主题自动适配。

## 使用方法

1. 打开【应用设置】→【DeepSeek 余额】。
2. 输入 DeepSeek API 密钥（以 `sk-` 开头）并点击「添加并验证」。插件会调用 DeepSeek 余额接口校验密钥，验证通过后才会加入列表。
3. 在主界面添加「DeepSeek余额」组件。
4. 在组件设置中选择要展示的 API。

## 说明

- 仅支持 DeepSeek 平台（`https://api.deepseek.com`）的 API 密钥。
- API 密钥以明文形式保存在插件配置目录的 `Settings.json` 中，请注意保管该文件。
- 若查询失败（如网络异常），组件会保留上一次的余额显示，避免界面反复跳动。

## 实现说明

- 目标框架：`net10.0`，依赖 `ClassIsland.PluginSdk` 2.1.1.1（Avalonia 12 / FluentAvalonia 3）。
- 界面使用 Avalonia 纯代码构建，仅使用通用控件，兼容不同 FluentAvalonia 版本。
