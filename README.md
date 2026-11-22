# Break Out（打砖块）

一个 Unity 2022 URP 做的打砖块小游戏。

## 玩法
- 玩家发射子弹攻击砖块
- 球撞碎砖块得分，砖块漏到底线扣命
- 一定时间进入下一波

## 操作
- 鼠标左键：发射小球
- ESC：暂停 / 退出

## 技术点
- Unity 2022.3 + URP
- C#：发射（Instantiate + Rigidbody.velocity）、碰撞（OnCollisionEnter）、波次生成、计分 UI
- 音效：发射 / 击碎 / 失败

## 下载 / 在线玩
- Windows 版：<https://github.com/2538182106-maker/Break-Out-Game/releases/tag/1.2.3.4>
- 演示视频：<B站链接>
