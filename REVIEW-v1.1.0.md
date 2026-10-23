# v1.1.1 实机验收记录

> 验收时间：2026-10-23 19:0x　执行：DSH 内的 AI 助手（DeepSeek-V4.1-Flash）
> 环境：Windows x64，2560×1600 @150%，DSH 桌面端 0.2.1-rc.2
> 方法：插件自带 `TestSurface.exe`（无害测试窗口）+ 其状态文件客观核对；不靠肉眼判断成败
> 备注：验收对象为 **v1.1.1**（v1.1.0 因 capture 输出含 `undefined` 被 DSH 判为非 lossless JSON，整插件不可用，已修）

## 结论

| # | 检查 | 结果 |
|---|---|---|
| 1 | 区域放大 | **通过** |
| 2 | 失效补拍 | **通过** |
| 3 | 输入判定 | **部分通过**（注入输入正常；真实输入的 `interrupted` 状态未观测到） |
| 4 | 批次上限 | **通过** |

## 1. 区域放大 —— 通过

| 步骤 | 返回 |
|---|---|
| 整屏 | `captureMode: "full"`, 2560×1600 |
| 区域 scale 1 | `captureMode: "region"`, 840×380, `region: {x:60,y:400,w:840,h:380}` |
| 区域 scale 2 | `captureMode: "region"`, 640×260, `region: {x:70,y:620,w:320,h:130}`（2× 放大正确） |
| 用 2× 图坐标 (320,130) 点击 | 命中真实按钮，测试窗口 `点击: 0 → 1` |
| 每批后 | 自动回到 `captureMode: "full"` |

坐标回映射正确，无需手动加偏移。

## 2. 失效补拍 —— 通过

用一张**已被取代**的 captureId 提交无害点击：

```json
{"status":"stale_capture","completed":0,"total":1,
 "summary":"截图缺失、过期或不是最新截图；本批未执行…",
 "captureMode":"full","budget":{"used":2,"remaining":78,"completedActions":2}}
```

- 动作**没有执行**（测试窗口点击计数保持不变）
- 自动附带**新的整屏图**与新 captureId
- **预算未扣**（失效批次不计入）

## 3. 输入判定 —— 部分通过

**注入输入不误判**：连续 4 批（含区域放大点击、跨窗口点击）全部 `executed`，没有被自身注入判成用户输入。

**真实输入仍拦截**：在提交前动鼠标后：

```json
{"status":"stale_capture","staleReason":"截图后发生真实用户输入。","completed":0,"total":5}
```

**未观测到 `interrupted`**：真实输入落在“截图 → 提交动作”的间隙里，被判为 `stale_capture`，而不是预览/执行中的 `interrupted`。1 秒预览窗口对人手太短，模型侧每次工具调用又有数秒延迟，人工难以精确卡点。

建议：验证 `interrupted` 需要一个能把预览拉长的开关（如 `previewMs` 可配），或用脚本在预览期间持续注入真实 HID 输入。当前行为（真实输入优先、本批不执行、附新图）本身是安全的。

## 4. 批次上限 —— 通过

设置 `MaxBatches = 2` 并保存（保存后计数重置：`used 0 / remaining 2`）。

| 批次 | 返回 |
|---|---|
| 第 1 批 | `executed`，`budget: used 1 / remaining 1` |
| 第 2 批 | `executed` + `limitReached: true`，`remaining 0`，摘要“已到 2 批上限…已结束操控”，覆盖层关闭 |
| 第 3 批 | `status: "batch_limit"`, `completed: 0`，提示“当前批没有执行，覆盖层已关闭” |
| `finish` 后 | `budget` 仍为 `used 2 / remaining 0` —— finish 没有在同一轮清零 |

## 仍然未测 / 未实现

- **未测**：其他物理分辨率、不同 DPI/输入权限组合、UAC 安全桌面、锁屏、受保护或独占全屏、跨显示器。
- **未实现**（原 REVIEW.md）：P0-2 自动点后局部复核、P1-5 预览时长可配、P2-6 安全前缀、P2-7 人工完成后画面变化提示、其余 P3 建议。

## 与 v1.0.1 评审的关系

`REVIEW.md` 的 13 项实测属于 **v1.0.0**；本文件的 4 项属于 **v1.1.1**。v1.1.1 修的是 P0-1、P1-3、P1-4、P2-8 四项，其余仍待处理。
