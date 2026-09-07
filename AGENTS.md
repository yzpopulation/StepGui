# StepGui 项目记忆

## 目标
- 自用证书管理 GUI（C# WinForms, .NET Framework 4.7.2），作为 step.exe（Smallstep CLI 0.30.6，位于项目目录）的外壳：所有证书操作（根/中间/网站证书支持通配符与 IP、代码签名证书、PFX 打包、导入外部证书并查看）全部通过传参调用 step.exe 完成，不在 C# 里做密码学。
- UI 迭代方向：左侧 TreeView 分类挂链管理证书、5 个功能标签页、DPI 兼容（125% 与 100% 都要正确）。

## 路径与环境（一律相对路径）
- 本目录（AGENTS.md 所在目录）即解决方案目录：`StepGui.slnx`，主工程 `StepGui\StepGui.csproj`（v4.7.2，WinExe）。
- MSBuild：VS 安装目录下 `MSBuild\Current\Bin\MSBuild.exe`（VS 18 Community 默认位于 `C:\Program Files\Microsoft Visual Studio\18\Community\`）。
- 构建：`MSBuild.exe StepGui\StepGui.csproj /p:Configuration=Debug /v:m /nologo`。本目录不是 git 仓库。
- 用户配置：`%LocalAppData%\StepGui\config.txt`（行格式 `step=...` / `store=...`）。
- 证书库默认在 `StepGui\bin\Debug\certs`。**绝不可删除 bin 目录**（证书库在里面）；清缓存只删 obj。
- 截图诊断脚本在 %TEMP% 下的 kilo 目录（shot*.ps1：Start-Process + GetWindowRect + CopyFromScreen/PrintWindow 保存 PNG 后 kill 进程；PrintWindow 方式不受窗口遮挡影响，优先用）。临时文件可能已清理，按此方法可随时重建。

## step.exe 命令事实（全部实测 exit=0）
- 根证书：`certificate create <CN> x.crt x.key --profile root-ca --no-password --insecure|--password-file <f> --not-after 87600h <密钥参数>`
- 中间证书：`--profile intermediate-ca --ca <root.crt> --ca-key <root.key> [--ca-password-file <f>]`（根私钥加密时必需）
- 叶子：`--profile leaf --san "*.x.com" --san "192.168.1.10"`（subject 自动含于 SAN；`--bundle` 可用）
- 代码签名：`certificate create <CN> c.crt c.key --template <tpl.json> --ca <int.crt> --ca-key <int.key>`，模板内容 `{"subject": {{ toJson .Subject }}, "keyUsage": ["digitalSignature"], "extKeyUsage": ["codeSigning"]}`（UTF8 无 BOM 写 %TEMP%，finally 删除）。**`--template` 与 `--profile` 互斥**；带 --ca/--ca-key 即为 CA 签发叶子证书，实测 EKU=codeSigning。
- PFX：`certificate p12 out.p12 crt key --ca <ca> [--password-file <f>|--no-password --insecure] [--legacy] --force`（子命令是 `p12`，`pfxform`/`p12form` 不存在）
- 链验证必须先捆绑：`certificate bundle leaf.crt int_ca.crt chain.crt` 再 `certificate verify chain.crt --roots root.crt`（叶子直接对 root verify 会报 unknown authority）
- 查看：`certificate inspect <f> --format text|json`；DER 先 `certificate format f.der --out f.pem -f`；inspect 不支持 P12/PFX；指纹：`certificate fingerprint <f>`
- 密钥参数：`--kty RSA --size 2048|3072|4096`、`--kty EC --curve P-256|P-384|P-521`、`--kty OKP --curve Ed25519`

## inspect JSON 与分类规则
- 关键字段（ZMap 风格）：`subject_dn`、`issuer_dn`、`subject.common_name`（数组）、`extensions.basic_constraints.is_ca`（叶子证书没有该扩展）、`signature.self_signed`、`validity.end`（ISO 时间）、`subject_key_info.key_algorithm.name`。
- 分类规则：is_ca+self_signed=根证书；is_ca+!self_signed=中间证书；else=网站证书。树按 issuer_dn==subject_dn 挂链。

## 当前 UI 结构（全部在 Form1.BuildUi() 运行时构建）
- 窗口：运行时设 ClientSize(1150,720)/MinimumSize(980,620)，5 个标签页顺序：根证书/中间证书/网站证书/代码签名/证书详情（详情页用 `_detailsPage` 字段引用，勿用 TabPages[3] 硬编码索引）。
- 左栏：外层 TLP 行 0 = Absolute S(90) 按钮条（TableLayoutPanel 2x2 网格，2 列各 50%，按钮 Dock=Fill 等宽，`BarBtn` 辅助；**bar 必须显式设 Height=2*S(45)**），行 1 = Percent 100 的 TreeView；按钮组与树之间留 S(5) 空白（外层行高 S(95)）。
- 右侧 Grid：2 列（label S(165) + main 100%）；路径行用 `PathField`（Panel：先 Add textbox Dock=Fill，再 Add button Dock=Right Width=S(78)、AutoSize=false）；行高全部固定（Row 32 / RowAction 44 / RowFlow 40 / SAN 行 116），在 `NextRow` 内统一 S() 缩放。
- 密钥类型默认 RSA 2048：4 个 tab 的 `_xxKty = ComboDrop(...)` 均以 "RSA 2048" 为第一项（即默认），顺序 RSA 2048/3072/4096/EC P-256/384/521（Ed25519 仅根/中间有）。
- 诊断日志（保留，有用）：ClassifyFileAsync 输出 `分类: name ext=… → Kind (CN=…)`，catch 里输出 `分类异常 name: ex.Message`。

## DPI 兼容（125% 与 100% 双兼容，已实测）
- `Program.Main` 开头 P/Invoke `SetProcessDPIAware()`（消除位图拉伸模糊）；Designer 用 `AutoScaleMode=None`（无 AutoScaleDimensions/ClientSize/MinimumSize）。
- Form1 ctor 用 `Graphics.FromHwnd(IntPtr.Zero).DpiX` 取系统 DPI 存静态 `_dpi`，`S(int)`/`SSize(int,int)` 按 dpi/96 缩放一切固定像素（窗口尺寸、SplitterDistance 260、Panel1/2MinSize 240/400、SplitterWidth 6、日志行 180、label 列 165、Grid Padding、PathField 按钮 78、SAN MinimumSize 100、按钮条行高 45/90 等）。100% 时 S()=恒等。

## 关键坑（务必遵守）
- **TLP 行高禁用 AutoSize**：TableLayoutPanel 会用默认字体预计算并缓存行高（隐藏标签页/字体未继承时尤甚），导致按钮过矮、文字被裁。所有 RowStyle 用 Absolute + S()。此坑已踩三次：右侧 Grid 行高、左栏按钮条行高。
- **Dock=Top 的 TLP 默认 Height=100px**：不显式设 Height 时高 DPI 下内容被裁（下排按钮底边消失）。必须 `Height = 各行绝对高之和`。
- **JavaScriptSerializer 的 JSON 数组是 ArrayList（非泛型 IEnumerable）**：`as IEnumerable<object>` 必然失败。解析 inspect JSON 一律用 `Get(dict,key)`（TryGetValue 安全取值），禁止裸下标（叶子证书无 basic_constraints 曾导致 KeyNotFoundException 被吞掉、全部叶子归入"其他"）；取数组元素先 `as string`、再 `as System.Collections.IEnumerable` 兜底。
- SplitContainer 必须先加入布局（Controls.Add）再设 Panel2MinSize/SplitterDistance，且包 try/catch。
- 密码经临时文件传递：`StepCli.WritePasswordFile`（UTF8 无 BOM、无换行），finally 中 `DeletePasswordFile`；StepCli 已做 ANSI 清理（`\u001B\[[0-9;]*[A-Za-z]`）、超时 180s。
- **VS 设计器报"无法设计基类 System.Void"**：非代码问题（csproj/partial/resx 均标准），诱因是 VS 开着时被命令行 MSBuild 外部重建，设计器程序集缓存失配。处理：VS 内"重新生成解决方案"后重开设计器，不行则关 VS 清 obj 重开。设计器只会显示空白窗体（UI 全在运行时构建），对本项目无实际用处。

## 状态
- 已完成：全部功能 + DPI 双兼容 + 分类/挂链/代码签名端到端实测通过（含 UI 级点击验证）；测试产生的 codesign.crt/key 已在证书库中。
- Blocked：(none)

## 下一步（可选）
- 若需系统 DPI 运行中变更的兼容，处理 WM_DPICHANGED（当前为启动时系统 DPI，单屏够用）。

## 相关文件
- `StepGui\Form1.cs`：主窗体全部 UI 与逻辑（BuildUi/BuildLeftPanel/BuildRootTab/BuildIntTab/BuildLeafTab/BuildCodeTab/BuildDetailsTab/Grid/Row/RowAction/RowFlow/RowFill/PathField/BarBtn/S/SSize/RefreshTreeAsync/ClassifyFileAsync/ParseCertMeta/Get/ExtractCn/BuildTree/FileNode/各生成与导入导出 handler）。
- `StepGui\StepCli.cs`：step.exe 进程调用封装（Quote/StripAnsi/WritePasswordFile/RunAsync）。
- `StepGui\AppConfig.cs`：路径持久化与证书库目录管理。
- `StepGui\Form1.Designer.cs`：窗体字体（Microsoft YaHei UI 9F）与 AutoScaleMode=None 设置。
- `StepGui\StepGui.csproj`：含 AppConfig.cs/StepCli.cs/System.Web.Extensions 引用；Form1.cs 有 SubType=Form。
- `StepGui\step.exe`：Smallstep CLI 0.30.6，build 后复制到 bin\Debug。
- `StepGui\bin\Debug\StepGui.exe`：编译产物，冒烟测试对象（进程存活验证）。
