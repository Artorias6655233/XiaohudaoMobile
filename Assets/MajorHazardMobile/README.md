# 重大危险源安全管理 · 安卓手机 / 平板版

从 AR 眼镜工程（Xiaohudao2ARPlatform/UnityProjects，2.AR-Page「重大危险源」）移植，接口和业务逻辑相同，交互改为触屏。

## 使用
1. 菜单 **Tools/MajorHazard/Setup Mobile Scene**：配置 URP、生成场景 `MajorHazardMobile.unity`（设为唯一构建场景）、安卓发布设置（包名 `com.dm.xiaohudao.majorhazard`，自动旋转，IL2CPP ARM64，minSdk 24）。可重复执行。
   之后若模型有粉色材质，执行 **Tools/MajorHazard/Fix Pink Materials (URP)**：重导模型（FBX 内嵌材质是在内置管线下生成的 Standard）、把 Standard / Legacy / 丢失的着色器换成 URP Lit，剩余问题输出到控制台。
2. File/Build Settings 切到 Android 出包。编辑器里直接 Play 也能用（鼠标操作，定位用模拟位置）。

场景里只有一个 `HazardMobileApp` 物体（引用字体、`ArtWorks/XHD.prefab`、登录页两张图），界面全部运行时代码生成。

启动先显示登录页 `LoginScreen`（移植自 AR 端 `Login-Page.prefab` + `Tiger.UI.LoginView`，逻辑相同：预填 tiger / 123456，「获取验证码」未接入，点「登录」直接成功），登录后才显示重大危险源首页。横屏按原预制体 1436×764 布局等比缩放，竖屏手机改为上下排列。贴图在 `Textures/`（与原工程同 guid）。

## 与 AR 端的对应
| 页面 | AR 端 | 移动端改动 |
|---|---|---|
| 安全监督自适应查看 `SafetySupervisionPage` | `SafetySupervisionPanel` 手势表格 + 翻页 | 滑动表格，滑到底自动加载下一页；标签筛选；点行弹详情 |
| 智能监测预警 `HazardAlarmPage` | `HazardAlarmPanel` VLP 现实画面闪烁 POI | 报警设备在三维地图上闪烁标注；新报警 Toast + 振动 |
| 智能风险管控 `RiskControlPage` | `RiskControlPanel` VLP 离我最近 + 手势 / 语音改等级 | 手机 GPS 测距 + 指南针箭头；点按钮改等级，确认对话框后提交 |
| 智能风险评估 `RiskAssessmentPage` | `RiskAssessmentPanel` 语音调取 / 语音逐项填写 | 搜索框调取 + 表单填写（输入法自带语音输入可用），危险源从可搜索列表选 |
| 重大危险源 `MajorHazardTablePage` | `MajorHazardTablePanel` 手势表格 + 语音查询 | 滑动表格，关键词搜索 + 等级标签筛选 |
| 重大危险源三维地图查看 `MajorHazardMapPage` | `MajorHazardMapPanel` Map3DViewer + Rokid 手势 | 单指旋转、双指缩放 / 平移 / 扭转、双击复位；浮动 ＋/－/复位按钮 |

写平台数据的操作（改风险等级、平台模式下保存评估报告）都要在确认对话框里确认。评估报告默认演示模式（只存本机）。

## 目录
- `Scripts/Platform`、`Scripts/Data` — 从 AR 端原样复制（`NsxhcipSession` / `NsxhcipJeecgSession` / `HazardApi` / `HazardCache` / `HazardData` / `HazardJson` / `RiskReportDemoStore` / `HazardAlarmBlink`），命名空间不变，方便与 AR 端对比同步。`HazardSites` 去掉了语音匹配，改为 `Search`。
- `Scripts/Map3D` — AR 端 `Map3DViewer` 去掉语音口令和眼镜焦点、加平移；`Map3DTouchControl` 替代 Rokid 手势的 `Map3DOrbitControl`。
- `Scripts/UI` — 移动端 UI 工具（`MUI`、表格 `MTable`、标签组 `ChipGroup`、浮层 `MOverlay`、按 dp 缩放的 `ResponsiveCanvas`）。
- `Scripts/Pages` — 6 个功能页（基类 `HazardPage`：横屏左右分栏，竖屏上下排列）。
- `Scripts/App` — 入口 `HazardMobileApp`、定位 `DeviceGeoLocation`。
- 三维模型及依赖在 `Assets/ArtWorks`、`Assets/JuHao`（与 AR 工程同路径同 guid）；URP 配置在 `Assets/Settings`。
