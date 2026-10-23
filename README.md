# Shinelon TN15S Mode Indicator

神舟炫龙 TN15S 的 Windows 通知区域性能模式指示器。程序仅显示状态，不控制性能模式、CPU 睿频或键盘灯。

## 图标含义

- 绿色闪电：性能模式开启。
- 灰色斜线：性能模式关闭。
- 黄色问号：状态尚未确定。
- 红色感叹号：频率计数器读取失败。

程序启动后显示在 Windows 右下角通知区域。Windows 可能先将新图标放入 `∧` 折叠菜单，可手动拖到电池、网络图标旁边。右键图标可退出。

## 状态判定

1. 启动时读取 `Win32_PerfFormattedData_Counters_ProcessorInformation` 的 `_Total.PercentofMaximumFrequency`。
2. 严格大于 `80%` 时显示开启，否则显示关闭。
3. 收到本机 `root\wmi:AcpiTest_EventULong` 事件后，以事件结果为准：
   - `ACPI\PNP0C14\1_0`、值 `49`：性能模式关闭。
   - `ACPI\PNP0C14\1_0`、值 `50`：性能模式开启。

`80%` 阈值是针对这台 TN15S 的启动估算。CPU 实时频率会受系统负载和 Windows 电源策略影响，因此它不是通用的硬件模式接口。

## 使用方法

直接运行：

```powershell
.\dist\Shinelon-ModeIndicator.exe
```

注册当前用户登录自启动：

```powershell
.\dist\Shinelon-ModeIndicator.exe --install
```

删除登录自启动：

```powershell
.\dist\Shinelon-ModeIndicator.exe --uninstall
```

自启动项位于：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

名称为 `ShinelonModeIndicator`。程序使用本地命名互斥体，重复启动不会产生多个图标。

## 编译环境

- Windows 10 x64
- .NET Framework 4.8
- Windows 自带的 x64 C# 编译器：`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- 本次发布使用的编译器文件版本：`4.8.9232.0`
- 目标：WinForms、x64、`winexe`
- 引用：`System.Management.dll`、`System.Drawing.dll`、`System.Windows.Forms.dll`

在仓库根目录执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本会生成 `dist\Shinelon-ModeIndicator.exe`，并同步更新 `SHA256SUMS.txt`。

## 适用范围

当前只针对已经实测的神舟炫龙 TN15S（Windows 10 x64）事件接口。其他炫龙、神舟、CLEVO 或同方机型即使外观相近，也不保证事件 GUID、实例名或数值相同。

## 许可证

[MIT](LICENSE)
